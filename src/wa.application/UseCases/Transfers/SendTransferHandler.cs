using System.Net.Mail;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-002-04 (TA-4.2#3) — the only business logic behind POST /api/v1/transfers/{id}/send:
/// activate a Draft transfer (Status 0→1), store normalized EmailRecipient rows, emit transfer.created via outbox.
/// Returns null when the transfer is not found — the endpoint throws BusinessException(ProblemCodes.NotFound).
/// US-020-02: limits from IPlanContextAccessor; account active-transfers re-check.
/// </summary>
public sealed class SendTransferHandler : IRequestHandler<SendTransferCommand, SendTransferResult?>
{
    private readonly ISendStore _store;
    private readonly ITransferStore _transferStore;
    private readonly IPlanContextAccessor _planContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<SendTransferHandler> _logger;
    private readonly IAppUserStore _users;   // T-008-08 (FR-008-7): resolve the display-name "From" default

    /// <summary>Initializes a new instance of the <see cref="SendTransferHandler"/> class.</summary>
    /// <param name="store">Send-side store that loads the draft and persists the sent transfer.</param>
    /// <param name="transferStore">Store used to count the owner's active transfers and storage.</param>
    /// <param name="planContext">Accessor for the request's plan limits and features.</param>
    /// <param name="eventPublisher">Publishes the <c>transfer.created</c> event.</param>
    /// <param name="telemetry">Reporter for the storage telemetry metric.</param>
    /// <param name="logger">Logger injected for the handler (currently unused).</param>
    /// <param name="users">Store used to resolve the signed-in sender's display name.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="users"/> is null.</exception>
    public SendTransferHandler(
        ISendStore store,
        ITransferStore transferStore,
        IPlanContextAccessor planContext,
        IEventPublisher eventPublisher,
        WaTelemetryReporter telemetry,
        ILogger<SendTransferHandler> logger,
        IAppUserStore users)   // T-008-08 (FR-008-7): session-owner display-name resolution — registered singleton at Program.cs L107
    {
        _store = store;
        _transferStore = transferStore;
        _planContext = planContext;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <inheritdoc />
    public async Task<SendTransferResult?> Handle(SendTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Load transfer; null → endpoint maps to 404 NOT_FOUND.
        var transfer = await _store.FindByIdAsync(request.TransferId, cancellationToken);
        if (transfer is null)
            return null;

        // 2. Only a Draft can be sent. Double-send with a different idempotency key is an edge case → 500 INTERNAL via GlobalErrorMiddleware.
        if (transfer.Status != 0)
            throw new InvalidOperationException($"Transfer {transfer.Id} already sent (status={transfer.Status}).");

        // 3. Limits of the session user's current plan at send time — never a literal (TA-3.4, FR-020-2).
        var plan = await _planContext.GetAsync(cancellationToken);
        var limits = plan.Limits;

        // 3b. T-007-04 / AC-007-1: send-time re-check — a flag may have changed since finalize; checks precede mutation.
        //     Strict > : exactly-at-limit passes (inclusive cap, boundary pinned in unit tests).
        if (transfer.TotalBytes > limits.MaxTransferSize)
            throw new TransferSizeExceededException(transfer.TotalBytes, limits.MaxTransferSize, 0, limits.MaxSingleFile);

        // 3c. US-020-02 (FR-020-2, AC-020-1/2): account-only active-transfers re-check — guests skip (FR-020-4).
        //     Checks precede mutation; existing active transfers are never touched (they finish their natural life).
        if (request.OwnerAppUserId is { } owner && !limits.IsActiveTransfersMaxInfinite())
        {
            var activeCount = await _transferStore.GetActiveTransferCountAsync(owner, cancellationToken);
            if (activeCount >= limits.ActiveTransfersMax)
                throw new ActiveTransfersExceededException(activeCount, limits.ActiveTransfersMax);
        }

        // 3d. US-022-01 (FR-022-3): scheduling is a Pro+ feature (plan row AND global gate) — checked before any mutation.
        //     Guests resolve to the Free plan → forbidden.
        if (request.ScheduledSendAtUtc is not null && !plan.Features.Scheduling)
            throw new ScheduleForbiddenException();

        // 3e. US-022-03 (FR-022-2): window re-validated against SERVER time (client clock skew defense), inclusive, before mutation.
        var now = DateTime.UtcNow;
        if (request.ScheduledSendAtUtc is { } due && !ScheduleWindow.Contains(due, now))
            throw new ScheduleOutOfWindowException();

        // 4. Parse recipients: split on [\r\n,;\s]+, trim, drop empties, validate per-address, lowercase, dedupe, cap.
        var addresses = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in request.Emails)
        {
            foreach (var token in Regex.Split(raw, @"[\r\n,;\s]+"))
            {
                var trimmed = token.Trim();
                if (trimmed.Length == 0) continue;
                if (!IsValidEmail(trimmed)) continue; // invalid → dropped (FR-002-6: valid ones proceed)
                var lower = trimmed.ToLowerInvariant();
                if (seen.Add(lower))
                    addresses.Add(lower);
            }
        }

        if (addresses.Count > limits.MaxEmails)
            throw new TransferValidationException($"Maximum {limits.MaxEmails} recipients allowed.");

        // 5. Stamp send-time limits once (FR-018-4: retention + download cap; live rows never re-resolve), then activate.
        SendTimeLimits.Stamp(transfer, limits, now);
        // US-022-01 (FR-022-4/7): ExpiresAt was stamped at send time by SendTimeLimits.Stamp above; a scheduled transfer is live now (Status 5).
        transfer.ScheduledSendAtUtc = request.ScheduledSendAtUtc;
        transfer.Status = request.ScheduledSendAtUtc is null ? TransferStatus.Active : TransferStatus.Scheduled;

        // T-008-08 (FR-008-7): send-time attribution — the session user id IS the owner (guest → null).
        // Unconditional overwrite is deliberate: a guest-created draft sent while signed in becomes attributed,
        // and a guest send stays NULL no matter how the draft was created. Lands ahead of T-022's My Files list.
        transfer.OwnerAppUserId = request.OwnerAppUserId;

        // T-008-08 (US-008-04 happy path step 2): "From" defaults to the display name. The per-transfer override
        // (request.SenderName) still wins (exit check d); else the signed-in user's display name with the T-008-06
        // email-local-part fallback for an empty/blank name; guests stay "Anonymous". SenderName is a SNAPSHOT at
        // send time — later profile edits never rewrite it (FR-002-4).
        transfer.SenderName = request.SenderName ?? await ResolveSenderName(request.OwnerAppUserId, cancellationToken);
        transfer.SenderEmail = request.SenderEmail;
        transfer.Note = request.Note;

        // 5b. T-002-06: password hash — PBKDF2 via ASP.NET Identity PasswordHasher<string> (TA-9.1). Null → column stays null.
        if (!string.IsNullOrEmpty(request.Password))
        {
            var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
            transfer.PasswordHash = hasher.HashPassword(transfer.Id.ToString("n"), request.Password);
        }
        else if (transfer.SourcedFrom is not null)
        {
            // T-010-03 (US-010-02): re-send drafts clear their copied hash when no new password is provided — the original's
            // PBKDF2 key (original transfer ID) doesn't match this draft's ID, so keeping it would break unlock.
            transfer.PasswordHash = null;
        }

        // 6. One EmailRecipient row per unique address.
        var recipients = addresses.Select(a => new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transfer.Id,
            Address = a
        }).ToList();

        // 7. Build the TA-5.3 transfer.created envelope (exact payload shape).
        var payload = new
        {
            transferId = transfer.Id,
            linkId = transfer.LinkId,
            totalBytes = transfer.TotalBytes,
            fileCount = transfer.FileCount,
            emails = addresses,
            senderEmail = transfer.SenderEmail,
            scheduledSendAt = transfer.ScheduledSendAtUtc
        };

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.TransferCreated,
            Version = 1,
            OccurredAtUtc = now,
            CorrelationId = GetCorrelationGuid(),
            PartitionKey = transfer.LinkId,
            Payload = payload
        };

        // 8. Persist: Transfer update + EmailRecipient rows in one transaction.
        await _store.SaveSendAsync(transfer, recipients, cancellationToken);

        // 9. Emit transfer.created via the outbox (separate operation — TA-4.1.7 at-most-once accepted at M0).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        // T-007-05 / AC-007-2 (meter): storage_bytes_active — re-run the FR-007-4 SUM expression after commit;
        // Status 0→1 puts this row into the expression at send time, so the recomputed value IS FR-007-4's read.
        if (transfer.OwnerAppUserId is not null)
            _telemetry.StorageBytesActive(await _transferStore.GetActiveStorageBytesAsync(transfer.OwnerAppUserId.Value, cancellationToken));

        return new SendTransferResult(
            transfer.Id,
            transfer.LinkId,
            transfer.Status,
            transfer.ExpiresAtUtc,
            transfer.TotalBytes,
            transfer.FileCount,
            addresses,
            transfer.ScheduledSendAtUtc);
    }

    /// <summary>Derives the correlation Guid from the W3C trace-id (32 hex = 16 bytes). Falls back to a fresh Guid outside a request.</summary>
    private static Guid GetCorrelationGuid()
    {
        var traceId = CorrelationContext.CurrentRequest?.TraceId;
        if (traceId is not null && traceId.Length == 32)
        {
            var hex = traceId.Replace("-", "");
            if (hex.Length == 32 && System.Linq.Enumerable.All(hex, c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return new Guid(hex);
        }
        return Guid.NewGuid();
    }

    /// <summary>T-008-08 (FR-008-7 / US-008-04 step 2): default "From" for a signed-in send. Resolves the session owner's
    /// display name; an empty/blank name falls back to the email local part (T-008-06 rule — never "From: (blank)").
    /// Unknown or soft-deleted owner (e.g. account deleted mid-draft, T-008-09) → "Anonymous". Guest (null) → "Anonymous".</summary>
    private async Task<string> ResolveSenderName(Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is not null && await _users.GetByIdAsync(ownerAppUserId.Value, ct) is { } user)
        {
            var name = user.DisplayName.Trim();
            return string.IsNullOrEmpty(name) ? user.Email.Split('@')[0] : name;   // local-part fallback — mirrors AppUserStore.UpdateProfileAsync / Signup.DeriveDisplayName verbatim
        }
        return "Anonymous";
    }

    /// <summary>Validates an email address using .NET's MailAddress (TA-4.2#3: per-address validation).</summary>
    private static bool IsValidEmail(string address)
    {
        try
        {
            _ = new MailAddress(address);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
