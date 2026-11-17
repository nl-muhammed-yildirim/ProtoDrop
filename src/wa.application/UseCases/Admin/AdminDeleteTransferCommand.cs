using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-05 (TA-4.2#21 / FR-011-4): DELETE /api/v1/admin/transfers/{id} — SuperAdmin-only force-delete.
/// Reason is pinned to "admin" at the construction site (endpoint 21).
/// </summary>
public sealed record AdminDeleteTransferCommand(Guid Id) : IRequest<MarkDeleteResult?>;

/// <summary>
/// T-011-05 (TA-4.2a): mirrors DeleteTransferHandler at src/wa.application/UseCases/Transfers/DeleteTransferCommand.cs:34
/// verbatim except reason pinned to "admin" and the store call is AdminMarkDeletedAsync(Id, DateTime.UtcNow, ct).
/// null → return null (endpoint maps to NOT_FOUND); non-null → EventEnvelope { Type=EventType.TransferDeleted, Version=1,
/// PartitionKey=result.LinkId, Payload={transferId=Id, reason="admin"} } → PublishAsync.
/// </summary>
public sealed class AdminDeleteTransferHandler : IRequestHandler<AdminDeleteTransferCommand, MarkDeleteResult?>
{
    private readonly IAdminTransferStore _store;
    private readonly IEventPublisher _eventPublisher;

    public AdminDeleteTransferHandler(IAdminTransferStore store, IEventPublisher eventPublisher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public async Task<MarkDeleteResult?> Handle(AdminDeleteTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Store: platform-scoped predicate (Id=@id AND Status != 4) in ONE query → null on no match.
        var result = await _store.AdminMarkDeletedAsync(request.Id, DateTime.UtcNow, cancellationToken);
        if (result is null)
            return null;   // endpoint maps to NOT_FOUND — EC-009-2: a second delete returns the identical body as a first

        // 2. Build the TA-5.3 transfer.deleted envelope (SendTransfer.cs:160-169 shape verbatim — PartitionKey = the row's LinkId).
        var payload = new
        {
            transferId = request.Id,
            reason = "admin"   // T-011-05: pinned to "admin" (FR-011-4)
        };

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.TransferDeleted,   // member 3 of the TA-5.3 closed set — no new enum member
            Version = 1,
            OccurredAtUtc = result.DeletedAtUtc,
            CorrelationId = GetCorrelationGuid(),
            PartitionKey = result.LinkId,       // exactly like send (SendTransfer.cs:167)
            Payload = payload
        };

        // 3. Emit via the outbox — separate operation, TA-4.1.7 at-most-once accepted at M0 (SendTransfer.cs:175 pattern).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        return result;   // endpoint maps non-null → 204 No Content (no body)
    }

    /// <summary>
    /// Derives the correlation Guid from the W3C trace-id (32 hex = 16 bytes). Falls back to a fresh Guid outside a request.
    /// Mirrors DeleteTransferHandler.GetCorrelationGuid() verbatim.
    /// </summary>
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
