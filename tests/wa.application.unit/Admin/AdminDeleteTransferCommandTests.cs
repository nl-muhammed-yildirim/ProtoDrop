using System.Text.Json;
using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-05 (TA-4.2#21 / FR-011-4): DELETE /api/v1/admin/transfers/{id} — SuperAdmin-only force-delete.
// The handler's contract: null = already-deleted / unknown id (endpoint → 404 NOT_FOUND, EC-009-2);
// non-null = the row was soft-deleted and transfer.deleted emitted once via outbox with reason "admin" (TA-5.3).
public class AdminDeleteTransferCommandTests
{
    private readonly StubAdminTransferStore _store;
    private readonly StubEventPublisher _eventPublisher;
    private readonly AdminDeleteTransferHandler _handler;

    public AdminDeleteTransferCommandTests()
    {
        _store = new StubAdminTransferStore();
        _eventPublisher = new StubEventPublisher();
        _handler = new AdminDeleteTransferHandler(_store, _eventPublisher);
    }

    // ---- Exit check 1: happy path — Status=4 + DeletedAtUtc set, event emitted once with reason "admin" ----

    [Fact]
    public async Task Handle_Deletes_EmitsTransferDeletedOnce_WithReasonAdmin_AndPartitionKey()
    {
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 1));

        await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);

        // Emitted exactly once.
        var envelopes = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.TransferDeleted, envelopes.Type);
        // PartitionKey = the row's LinkId (SendTransfer.cs:167 idiom).
        Assert.Equal("ABCD1234", envelopes.PartitionKey);
        // Payload = { transferId, reason:'admin' } (TA-5.3 closed set — member 3 of EventType).
        var payloadJson = JsonSerializerHelper.PayloadJson(envelopes.Payload!);
        using var doc = JsonDocument.Parse(payloadJson);
        Assert.Equal(transferId, doc.RootElement.GetProperty("transferId").GetGuid());
        Assert.Equal("admin", doc.RootElement.GetProperty("reason").GetString());

        // Row is soft-deleted.
        var row = Assert.Single(_store.Transfers, t => t.Id == transferId);
        Assert.Equal(4, row.Status);
        Assert.NotNull(row.DeletedAtUtc);
    }

    // ---- Exit check 2: second delete of the same id → null (NOT_FOUND, EC-009-2) ----

    [Fact]
    public async Task Handle_SecondDelete_ReturnsNull_NoSecondEvent()
    {
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 1));

        // First delete succeeds.
        var first = await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);
        Assert.NotNull(first);

        // Second delete of the same id → null (the store's platform-scoped predicate found no match).
        var second = await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);
        Assert.Null(second); // endpoint maps to 404 NOT_FOUND — EC-009-2: client treats as success

        // No second event — exactly one transfer.deleted per row's lifetime.
        Assert.Single(_eventPublisher.Published);
    }

    // ---- Exit check 3a: sole-reference blob → PhysicallyDeletedAtUtc = now+24h, RefCount clamped to zero ----

    [Fact]
    public async Task Handle_SoleReferenceBlob_RefCountClampedToZero_PhysicallyDeletedAtUtcSet()
    {
        var transferId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 1));
        _store.BlobRefs.Add(new BlobRef { Id = blobId, RefCount = 1 }); // sole reference
        _store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transferId, BlobRefId = blobId, OriginalName = "a.txt", SizeBytes = 100, SortOrder = 0 });

        var before = DateTime.UtcNow;
        await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);

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
        var transferId = Guid.NewGuid();
        var blobId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 1));
        _store.BlobRefs.Add(new BlobRef { Id = blobId, RefCount = 2 }); // shared by two transfers
        _store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transferId, BlobRefId = blobId, OriginalName = "a.txt", SizeBytes = 100, SortOrder = 0 });

        await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);

        var blob = Assert.Single(_store.BlobRefs, b => b.Id == blobId);
        Assert.Equal(1, blob.RefCount); // decremented by 1 — still referenced by another transfer
        Assert.Null(blob.PhysicallyDeletedAtUtc); // untouched — the blob is NOT sole-referenced anymore
    }

    // ---- Admin-specific case: guest (null owner) → found + deleted (the whole point of dropping the owner predicate) ----

    [Fact]
    public async Task Handle_GuestTransfer_NullOwner_DeletesSuccessfully()
    {
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 1)); // guest-created (OwnerAppUserId = NULL)

        var result = await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);

        Assert.NotNull(result); // admin can target guest transfers where OwnerAppUserId is NULL
        var row = Assert.Single(_store.Transfers, t => t.Id == transferId);
        Assert.Equal(4, row.Status); // soft-deleted
        Assert.Single(_eventPublisher.Published); // event emitted once
    }

    // ---- Exit check 4: already-deleted (Status=4) → null (NOT_FOUND, no event) ----

    [Fact]
    public async Task Handle_AlreadyDeleted_ReturnsNull_NoEvent()
    {
        var transferId = Guid.NewGuid();
        _store.Transfers.Add(NewTransfer(transferId, "ABCD1234", ownerId: null, status: 4)); // already-deleted

        var result = await _handler.Handle(new AdminDeleteTransferCommand(transferId), CancellationToken.None);

        Assert.Null(result); // NOT_FOUND — the predicate requires Status != 4
        Assert.Empty(_eventPublisher.Published); // no event — the delete did not happen
    }

    // ---- Helpers ----

    private static class JsonSerializerHelper
    {
        public static string PayloadJson(object? payload) =>
            JsonSerializer.Serialize(payload);
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

    // ---- Stubs (hand-written, no Moq — mirrors T-009-04's style) ----

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    private sealed class StubAdminTransferStore : IAdminTransferStore
    {
        public List<Transfer> Transfers { get; } = [];
        public List<BlobRef> BlobRefs { get; } = [];
        public List<FileItem> FileItems { get; } = [];

        /// <summary>
        /// T-011-05 (TA-4.2#21): simulates the EF store's platform-scoped predicate and RefCount decrement idiom.
        /// Mirrors TransferStore.MarkDeletedAsync with the owner predicate removed (three bounded queries).
        /// </summary>
        public Task<MarkDeleteResult?> AdminMarkDeletedAsync(Guid id, DateTime deletedAtUtc, CancellationToken ct)
        {
            // 1. Load the row with platform-scoped predicate (Id=@id AND Status != 4 — NO owner predicate).
            var t = Transfers.FirstOrDefault(t => t.Id == id && t.Status != 4);
            if (t is null) return Task.FromResult<MarkDeleteResult?>(null);

            // 2. Soft-delete (TA-3.1: NO hard delete).
            t.Status = 4;
            t.DeletedAtUtc = deletedAtUtc;

            // 3. Decrement RefCount for each distinct BlobRefId.
            var blobIds = FileItems.Where(f => f.TransferId == id).Select(f => f.BlobRefId).Distinct().ToList();

            if (blobIds.Count > 0)
            {
                var nowPlusBuffer = deletedAtUtc.AddHours(24); // FR-011-4: same 24h buffer as expiry idiom

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
