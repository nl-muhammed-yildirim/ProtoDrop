using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-009-04 (TA-4.2#17 / FR-009-3): DELETE /api/v1/transfers/{id} — owner delete with the refcount path.
// The handler's contract: null = foreign owner / guest / already-deleted / unknown id (endpoint → 404 NOT_FOUND, EC-009-2);
// non-null = the row was soft-deleted and transfer.deleted emitted once via outbox (TA-5.3).
public class DeleteTransferCommandTests
{
    private readonly StubDeleteStore _store;
    private readonly StubEventPublisher _eventPublisher;
    private readonly DeleteTransferHandler _handler;

    public DeleteTransferCommandTests()
    {
        _store = new StubDeleteStore();
        _eventPublisher = new StubEventPublisher();
        _handler = new DeleteTransferHandler(_store, _eventPublisher);
    }

    // ---- Exit check 1: happy path — Status=4 + DeletedAtUtc set, event emitted once ----

    [Fact]
    public async Task Handle_OwnerDeletes_MarksStatusFour_AndSetsDeletedAtUtc()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId, status: 1)); // 3 files attached below

        var before = DateTime.UtcNow;
        var result = await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);

        Assert.NotNull(result);
        var row = Assert.Single(_store.Transfers, t => t.Id == transferId);
        Assert.Equal(4, row.Status); // soft-delete (TA-3.1) — NOT a hard delete
        Assert.NotNull(row.DeletedAtUtc);
        Assert.InRange(row.DeletedAtUtc!.Value, before, DateTime.UtcNow + TimeSpan.FromMilliseconds(5));
        // MarkDeleteResult carries DeletedAtUtc + the row's LinkId (the event's PartitionKey).
        Assert.Equal("ABCD1234", result!.LinkId);
        Assert.InRange(result.DeletedAtUtc, before, DateTime.UtcNow + TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task Handle_OwnerDeletes_EmitsTransferDeletedOnce_WithPartitionKeyAndPayload()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId, status: 1));

        await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);

        // Emitted exactly once.
        var envelopes = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.TransferDeleted, envelopes.Type);
        // PartitionKey = the row's LinkId (SendTransfer.cs:167 idiom).
        Assert.Equal("ABCD1234", envelopes.PartitionKey);
        // Payload = { transferId, reason:'user' } (TA-5.3 closed set — member 3 of EventType).
        var payloadJson = JsonSerializerHelper.PayloadJson(envelopes.Payload!);
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        Assert.Equal(transferId, doc.RootElement.GetProperty("transferId").GetGuid());
        Assert.Equal("user", doc.RootElement.GetProperty("reason").GetString());
    }

    // ---- Exit check 2: second delete of the same id → null (NOT_FOUND, EC-009-2) ----

    [Fact]
    public async Task Handle_SecondDelete_ReturnsNull_AndEmitsNoEvent()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId, status: 1));

        // First delete succeeds.
        var first = await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);
        Assert.NotNull(first);

        // Second delete of the same id → null (the store's owner+not-deleted predicate found no match).
        var second = await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);
        Assert.Null(second); // endpoint maps to 404 NOT_FOUND — EC-009-2: client treats as success

        // No second event — exactly one transfer.deleted per row's lifetime.
        Assert.Single(_eventPublisher.Published);
    }

    // ---- Exit check 3a: sole-reference blob → PhysicallyDeletedAtUtc = now+24h, RefCount clamped to zero ----

    [Fact]
    public async Task Handle_SoleReferenceBlob_RefCountClampedToZero_PhysicallyDeletedAtUtcSet()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId, status: 1));
        _store.BlobRefs.Add(new BlobRef { Id = blobId, RefCount = 1 }); // sole reference
        _store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transferId, BlobRefId = blobId, OriginalName = "a.txt", SizeBytes = 100, SortOrder = 0 });

        var before = DateTime.UtcNow;
        await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);

        var blob = Assert.Single(_store.BlobRefs, b => b.Id == blobId);
        Assert.Equal(0, blob.RefCount); // clamped to zero (negative is impossible)
        Assert.NotNull(blob.PhysicallyDeletedAtUtc);
        // 24h buffer: now+24h — same literal as ExpiryDeletionStore.cs:66.
        Assert.InRange(blob.PhysicallyDeletedAtUtc!.Value, before.AddHours(24).AddSeconds(-5), before.AddHours(24).AddSeconds(5));
    }

    // ---- Exit check 3b: shared blob (RefCount>1) → decremented by 1, PhysicallyDeletedAtUtc untouched ----

    [Fact]
    public async Task Handle_SharedBlob_RefCountDecremented_PhysicallyDeletedAtUtcUntouched()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId, status: 1));
        _store.BlobRefs.Add(new BlobRef { Id = blobId, RefCount = 2 }); // shared by two transfers
        _store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transferId, BlobRefId = blobId, OriginalName = "a.txt", SizeBytes = 100, SortOrder = 0 });

        await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);

        var blob = Assert.Single(_store.BlobRefs, b => b.Id == blobId);
        Assert.Equal(1, blob.RefCount); // decremented by 1 — still referenced by another transfer
        Assert.Null(blob.PhysicallyDeletedAtUtc); // untouched — the blob is NOT sole-referenced anymore
    }

    // ---- Exit check 4a: another user's id → null (NOT_FOUND, no enumeration) ----

    [Fact]
    public async Task Handle_ForeignOwner_ReturnsNull_NoEvent_NoMutation()
    {
        var trueOwner = Guid.NewGuid();
        var foreignOwnerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", trueOwner, status: 1));

        // A different user attempts to delete.
        var result = await _handler.Handle(new DeleteTransferCommand(transferId, foreignOwnerId, "user"), CancellationToken.None);

        Assert.Null(result); // NOT_FOUND — no FORBIDDEN (the row's existence is never revealed)
        Assert.Empty(_eventPublisher.Published); // no event — the delete did not happen
        var row = Assert.Single(_store.Transfers, t => t.Id == transferId);
        Assert.Equal(1, row.Status); // untouched — no mutation on foreign-owner attempt
    }

    // ---- Exit check 4b: guest (null owner) → null (NOT_FOUND, no enumeration) ----

    [Fact]
    public async Task Handle_Guest_NullOwner_ReturnsNull_NoEvent()
    {
        var ownerId = Guid.NewGuid();
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", null, status: 1)); // guest-created (OwnerAppUserId = NULL)

        // A signed-in user attempts to delete a guest's transfer.
        var result = await _handler.Handle(new DeleteTransferCommand(transferId, ownerId, "user"), CancellationToken.None);

        Assert.Null(result); // NOT_FOUND — the predicate requires OwnerAppUserId=@ownerId
        Assert.Empty(_eventPublisher.Published);
    }

    // ---- Exit check 4c: unknown id → null (NOT_FOUND) ----

    [Fact]
    public async Task Handle_UnknownId_ReturnsNull_NoEvent()
    {
        var ownerId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();

        var result = await _handler.Handle(new DeleteTransferCommand(unknownId, ownerId, "user"), CancellationToken.None);

        Assert.Null(result); // NOT_FOUND — no such row exists
        Assert.Empty(_eventPublisher.Published);
    }

    // ---- Helpers ----

    private static class JsonSerializerHelper
    {
        public static string PayloadJson(object? payload) =>
            System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static Transfer NewTransfer(Guid id, string linkId, Guid? ownerId, byte status) => new()
    {
        Id = id,
        LinkId = linkId,
        OwnerAppUserId = ownerId,
        Status = status,
        TotalBytes = 100,
        FileCount = 1,
        SenderName = "Test",
        CreatedAtUtc = DateTime.UtcNow
    };

    // ---- Stubs (hand-written, no Moq — mirrors T-009-03's style) ----

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    private sealed class StubDeleteStore : ITransferStore
    {
        public List<Transfer> Transfers { get; } = [];
        public List<BlobRef> BlobRefs { get; } = [];
        public List<FileItem> FileItems { get; } = [];

        // ITransferStore members the handler doesn't use — signature-only.
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<IdempotencyKey?>(null);
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) =>
            Task.FromResult<(List<MyTransferRow>, string?)>(([], null));
        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) => Task.FromResult<GetMyTransferResult?>(null);

        /// <summary>
        /// T-009-04 (TA-4.2#17): simulates the EF store's owner+not-deleted predicate and RefCount decrement idiom.
        /// Mirrors ExpiryDeletionStore.DeleteTransfersAsync's pattern for a SINGLE transfer (three bounded queries).
        /// </summary>
        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct)
        {
            // 1. Load the row with owner-scoped predicate.
            var t = Transfers.FirstOrDefault(t => t.Id == id && t.OwnerAppUserId == ownerId && t.Status != 4);
            if (t is null) return Task.FromResult<MarkDeleteResult?>(null);

            // 2. Soft-delete (TA-3.1: NO hard delete).
            t.Status = 4;
            t.DeletedAtUtc = deletedAtUtc;

            // 3. Decrement RefCount for each distinct BlobRefId.
            var blobIds = FileItems.Where(f => f.TransferId == id).Select(f => f.BlobRefId).Distinct().ToList();

            if (blobIds.Count > 0)
            {
                var nowPlusBuffer = deletedAtUtc.AddHours(24); // FR-009-3: same 24h buffer as expiry idiom

                foreach (var blob in BlobRefs.Where(b => blobIds.Contains(b.Id)))
                {
                    blob.RefCount -= 1;
                    if (blob.RefCount <= 0)
                    {
                        blob.RefCount = 0; // clamp to zero
                        blob.PhysicallyDeletedAtUtc = nowPlusBuffer;
                    }
                }
            }

            return Task.FromResult(new MarkDeleteResult(deletedAtUtc, t.LinkId));
        }
    }
}
