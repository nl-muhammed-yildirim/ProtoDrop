using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers in one transaction (D-1),
/// emit account_deleted via the outbox, then telemetry. Mirror RequestMagicLink's two-steps-in-one-handler shape (insert → publish).
/// </summary>
/// <param name="users">Store that soft-deletes the account and re-homes its transfers.</param>
/// <param name="eventPublisher">Publishes the <c>account.deleted</c> event.</param>
/// <param name="telemetry">Reporter for the <c>account_deleted</c> telemetry event.</param>
/// <param name="blobs">Blob store used to remove the user's organization logo.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
/// <param name="logger">Optional logger for the best-effort logo cleanup warning.</param>
public sealed partial class DeleteAccountHandler(
    IAppUserStore users,
    IEventPublisher eventPublisher,
    WaTelemetryReporter telemetry,
    IBlobStore blobs,
    TimeProvider timeProvider,
    ILogger<DeleteAccountHandler>? logger = null) : IRequestHandler<DeleteAccountCommand, DeleteAccountResult>
{
    private readonly IAppUserStore _users = users;
    private readonly IEventPublisher _eventPublisher = eventPublisher;
    private readonly WaTelemetryReporter _telemetry = telemetry;
    private readonly IBlobStore _blobs = blobs;
    private readonly ILogger<DeleteAccountHandler>? _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate: no row (unknown OR soft-deleted) → NOT_FOUND (404 at the endpoint).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // 2. Soft-delete + transfer re-home in ONE transaction (D-1). The event publish happens AFTER this commits —
        //    the row is already soft-deleted at that point; EC-054-1's outbox re-publish covers broker failure.
        var deletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var ok = await _users.SoftDeleteWithReHomeAsync(user.Id, deletedAtUtc, cancellationToken);
        if (!ok)
            throw MeProfileException.ForNotFound(); // race where a concurrent DELETE already committed — same not-found shape

        // 3. Build the TA-5.2/TA-5.3 account.deleted envelope EXACTLY as RequestMagicLink.Handle does (verbatim pattern).
        var payload = new { userId = user.Id };   // PII sweep reference (US-008-06 edge case / TA-5.3) — spec pins "carrying the userId"
        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.AccountDeleted,      // 9th of 12 members — already in the frozen list, untouched file
            Version = 1,
            OccurredAtUtc = deletedAtUtc,         // the same instant passed into SoftDeleteWithReHomeAsync (the fact happened then)
            CorrelationId = GetCorrelationGuid(), // copy RequestMagicLink's private static verbatim (same convention as SendTransferHandler)
            PartitionKey = user.Email,            // email — same partitioning as the other auth.* events (RequestMagicLink uses email too)
            Payload = payload
        };

        // 4. Emit via outbox (EC-054-1: IsPublished false → re-publish picks it up on broker failure).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        // 5. Telemetry: exactly ONE account_deleted after publish — already a member of the TA-10.2 closed set.
        _telemetry.LogEvent(TelemetryEvents.AccountDeleted);

        // 6. US-021-04 (decision 4): the org logo lives in the avatars container with no lifecycle rule — deleted with the account.
        //    The account is already deleted: a storage failure here must not turn it into a 500 (the blob is an orphan at worst).
        if (!string.IsNullOrEmpty(user.OrgLogoBlobPath))
        {
            try
            {
                await _blobs.DeleteAsync(user.OrgLogoBlobPath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_logger is not null) LogOrgLogoDeleteFailed(_logger, user.Id);
            }
        }

        return new DeleteAccountResult();
    }

    /// <summary>Derives the correlation Guid from the W3C trace-id (same convention as SendTransferHandler — copied verbatim from RequestMagicLink).</summary>
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Org logo blob delete failed after account deletion for user {UserId}.")]
    private static partial void LogOrgLogoDeleteFailed(ILogger logger, Guid userId);
}
