using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-04 (TA-5.3 / FR-009-3): the store's delete result — the MediatR contract type for this use case.</summary>
public sealed record MarkDeleteResult(DateTime DeletedAtUtc, string LinkId);

/// <summary>
/// T-009-04 (TA-4.2#17): DELETE /api/v1/transfers/{id} — owner delete with refcount path. Reason is pinned at the
/// construction site (endpoint 17 passes "user"; US-011-02's admin force-delete will pass "admin" — one command, two reasons).
/// </summary>
public sealed record DeleteTransferCommand(Guid Id, Guid OwnerId, string Reason) : IRequest<MarkDeleteResult?>;

/// <summary>
/// T-009-04 (TA-4.2a): the only business logic behind DELETE /api/v1/transfers/{id} — mark the row Status=4 soft-delete,
/// decrement FileItem BlobRef RefCounts, emit transfer.deleted via outbox. Returns null when the store's owner-scoped
/// predicate found no match (foreign owner / guest / already-deleted / unknown id) — the endpoint maps null → 404 NOT_FOUND
/// (EC-009-2 idempotency: a second delete of an already-deleted row is indistinguishable from every other not-found shape).
/// </summary>
public sealed class DeleteTransferHandler : IRequestHandler<DeleteTransferCommand, MarkDeleteResult?>
{
    private readonly ITransferStore _store;
    private readonly IEventPublisher _eventPublisher;

    public DeleteTransferHandler(ITransferStore store, IEventPublisher eventPublisher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public async Task<MarkDeleteResult?> Handle(DeleteTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Store: owner+not-deleted predicate in ONE query → null on no match (owner scope / already-deleted / unknown).
        var result = await _store.MarkDeletedAsync(request.Id, request.OwnerId, DateTime.UtcNow, cancellationToken);
        if (result is null)
            return null;   // endpoint maps to NOT_FOUND — EC-009-2: a second delete returns the identical body as a first

        // 2. Build the TA-5.3 transfer.deleted envelope (SendTransfer.cs:160-169 shape verbatim — PartitionKey = the row's LinkId).
        var payload = new
        {
            transferId = request.Id,
            reason = request.Reason   // "user" at endpoint 17; US-011-02 admin reuses with "admin" (same command, different site)
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
    /// Mirrors SendTransfer.GetCorrelationGuid() verbatim (SendTransfer.cs:193).
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
