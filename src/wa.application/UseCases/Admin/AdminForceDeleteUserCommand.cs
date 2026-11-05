using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): DELETE /api/v1/admin/users/{id} — SuperAdmin-only force-delete.
/// Reuses T-008's GDPR soft-delete + re-home verbatim (reason "admin" is the admin context, not a column).
/// </summary>
public sealed record AdminForceDeleteUserCommand(Guid UserId) : IRequest<AdminForceDeleteResult?>;

/// <summary>
/// T-011-07: the write result — email captured PRE-delete so the endpoint can audit {email} after the row is soft-deleted
/// (IAppUserStore.GetByIdAsync filters DeletedAtUtc == null, so the email is unreadable post-delete).
/// </summary>
public sealed record AdminForceDeleteResult(Guid UserId, string Email);

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): force-delete a user — soft-delete + re-home via IAppUserStore.SoftDeleteWithReHomeAsync
/// (T-008-06, already exists), then emit account_deleted via the outbox EXACTLY as DeleteAccountHandler does.
/// null result → endpoint maps to 404 NOT_FOUND; non-null → endpoint audits {email} + returns 204 No Content.
/// </summary>
public sealed class AdminForceDeleteUserHandler : IRequestHandler<AdminForceDeleteUserCommand, AdminForceDeleteResult?>
{
    private readonly IAppUserStore _users;
    private readonly IEventPublisher _eventPublisher;

    public AdminForceDeleteUserHandler(IAppUserStore users, IEventPublisher eventPublisher)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public async Task<AdminForceDeleteResult?> Handle(AdminForceDeleteUserCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate: no row (unknown OR soft-deleted) → null (endpoint maps to 404 NOT_FOUND).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return null;

        // 2. Soft-delete + transfer re-home in ONE transaction (T-008-06 / FR-008-8 — same store call as the GDPR self-delete).
        var deletedAtUtc = DateTime.UtcNow;
        var ok = await _users.SoftDeleteWithReHomeAsync(user.Id, deletedAtUtc, cancellationToken);
        if (!ok)
            return null; // race where a concurrent DELETE already committed — same not-found shape

        // 3. Build the TA-5.2/TA-5.3 account.deleted envelope EXACTLY as DeleteAccountHandler L185-196 (verbatim pattern).
        var payload = new { userId = user.Id };   // PII sweep reference (US-008-06 edge case / TA-5.3) — spec pins "carrying the userId"
        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.AccountDeleted,      // 9th of 12 members — already in the frozen list, untouched file
            Version = 1,
            OccurredAtUtc = deletedAtUtc,         // the same instant passed into SoftDeleteWithReHomeAsync (the fact happened then)
            CorrelationId = GetCorrelationGuid(), // copy DeleteAccountHandler's private static verbatim (same convention as SendTransferHandler)
            PartitionKey = user.Email,            // email — same partitioning as the other auth.* events (RequestMagicLink uses email too)
            Payload = payload
        };

        // 4. Emit via outbox (EC-054-1: IsPublished false → re-publish picks it up on broker failure).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        // 5. Return the email PRE-delete so the endpoint can audit {email} after the row is soft-deleted.
        return new AdminForceDeleteResult(user.Id, user.Email);
    }

    /// <summary>Derives the correlation Guid from the W3C trace-id (same convention as SendTransferHandler — copied verbatim from DeleteAccountHandler).</summary>
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
}
