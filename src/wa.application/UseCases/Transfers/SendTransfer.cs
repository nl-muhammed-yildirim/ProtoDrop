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

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/send request (TA-4.2#3).</summary>
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null,
    Guid? OwnerAppUserId = null) : IRequest<SendTransferResult?>;   // T-008-08 (FR-008-7): session owner at send time — guest → null

/// <summary>POST /api/v1/transfers/{id}/send result (TA-4.2#3 response shape).</summary>
public sealed record SendTransferResult(
    Guid TransferId,
    string LinkId,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> Recipients);

/// <summary>
/// T-002-04: minimal DB surface the send handler needs.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface ISendStore
{
    Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct);
    Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct);
}

/// <summary>
/// T-002-04 (TA-4.2#3) — the only business logic behind POST /api/v1/transfers/{id}/send:
/// activate a Draft transfer (Status 0→1), store normalized EmailRecipient rows, emit transfer.created via outbox.
/// Returns null when the transfer is not found — the endpoint throws BusinessException(ProblemCodes.NotFound).
/// </summary>
public sealed class SendTransferHandler : IRequestHandler<SendTransferCommand, SendTransferResult?>
{
    private readonly ISendStore _store;
    private readonly ITransferStore _transferStore;
    private readonly ILimitsProvider _limits;
    private readonly IEventPublisher _eventPublisher;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<SendTransferHandler> _logger;
    private readonly IAppUserStore _users;   // T-008-08 (FR-008-7): resolve the display-name "From" default

    public SendTransferHandler(
        ISendStore store,
        ITransferStore transferStore,
        ILimitsProvider limits,
        IEventPublisher eventPublisher,
        WaTelemetryReporter telemetry,
        ILogger<SendTransferHandler> logger,
        IAppUserStore users)   // T-008-08 (FR-008-7): session-owner display-name resolution — registered singleton at Program.cs L107
    {
        _store = store;
        _transferStore = transferStore;
        _limits = limits;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    public async Task<SendTransferResult?> Handle(SendTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Load transfer; null → endpoint maps to 404 NOT_FOUND.
        var transfer = await _store.FindByIdAsync(request.TransferId, cancellationToken);
        if (transfer is null)
            return null;

        // 2. Only a Draft can be sent. Double-send with a different idempotency key is an edge case → 500 INTERNAL via GlobalErrorMiddleware.
        if (transfer.Status != 0)
            throw new InvalidOperationException($"Transfer {transfer.Id} already sent (status={transfer.Status}).");

        // 3. Resolve plan limits — never a literal (TA-3.4).
        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken);

        // 3b. T-007-04 / AC-007-1: send-time re-check — a flag may have changed since finalize; checks precede mutation.
        //     Strict > : exactly-at-limit passes (inclusive cap, boundary pinned in unit tests).
        if (transfer.TotalBytes > limits.MaxTransferSize)
            throw new TransferSizeExceededException(transfer.TotalBytes, limits.MaxTransferSize, 0, limits.MaxSingleFile);

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

        // 5. Activate the transfer: Status=1, expiry from RETENTION_DAYS, sender defaults.
        var now = DateTime.UtcNow;
        transfer.Status = 1;
        transfer.ExpiresAtUtc = now.AddDays(limits.RetentionDays);

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
            1, // Active
            transfer.ExpiresAtUtc,
            transfer.TotalBytes,
            transfer.FileCount,
            addresses);
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

/// <summary>
/// A validation failure in the send use case (e.g. MAX_EMAILS exceeded). The endpoint catches this
/// and throws BusinessException(ProblemCodes.Validation, ex.Message) — keeps the closed code list intact.
/// </summary>
public sealed class TransferValidationException : Exception
{
    public TransferValidationException(string message) : base(message) { }
}
