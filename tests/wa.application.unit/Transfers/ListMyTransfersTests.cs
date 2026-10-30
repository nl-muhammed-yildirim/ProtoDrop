using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Transfers;

namespace wa.application.unit.Transfers;

/// <summary>
/// T-009-01 (AC-009-1): ListMyTransfersHandler — cursor paging over ITransferStore.ListByOwnerAsync.
/// Hand-stub store only (T-008-06 style); no Moq, no EF. The stub simulates the store contract:
/// filter by OwnerId + cursor, order CreatedAtUtc desc, Take(limit+1), encode nextCursor from the last row.
/// </summary>
public class ListMyTransfersTests
{
    private static readonly Guid OwnerId = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime BaseUtc = new(2026, 10, 30, 18, 45, 12, DateTimeKind.Utc);

    public ListMyTransfersTests() { }

    // ---- AC-009-1: page 1 = exactly 25 rows ordered by CreatedAtUtc DESC (most recent first) ----
    [Fact]
    public async Task Page_ReturnsExactly25Rows_MostRecentFirst()
    {
        var store = new StubTransferStore();
        // Seed 30 owned transfers with strictly increasing CreatedAtUtc (i=0 oldest → i=29 most recent).
        for (int i = 0; i < 30; i++)
            store.Transfers.Add(new Transfer
            {
                Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, Status = 1,
                CreatedAtUtc = BaseUtc.AddSeconds(i * 60)
            });

        var handler = new ListMyTransfersHandler(store);
        var result = await handler.Handle(new ListMyTransfersQuery(OwnerId, null, 25), CancellationToken.None);

        Assert.Equal(25, result.Items.Count); // exactly 25 rows per TA-4.1.4 pin
        Assert.NotNull(result.NextCursor);   // there's a 30th row → next page exists
        // Most recent first: CreatedAtUtc strictly descending (the store orders by it; this pins the AC).
        for (int i = 1; i < result.Items.Count; i++)
            Assert.True(CreatedOf(store, result.Items[i - 1].Id) > CreatedOf(store, result.Items[i].Id),
                "Items must be ordered by CreatedAtUtc DESC");
    }

    // ---- AC-009-1 exit check: page 2 via cursor → remaining 5, NextCursor null, no dupes/gaps ----
    [Fact]
    public async Task Page2_ViaCursor_ReturnsRemaining5_NoDupesNoGaps()
    {
        var store = new StubTransferStore();
        for (int i = 0; i < 30; i++)
            store.Transfers.Add(new Transfer
            {
                Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, Status = 1,
                CreatedAtUtc = BaseUtc.AddSeconds(i * 60)
            });

        var handler = new ListMyTransfersHandler(store);
        var page1 = await handler.Handle(new ListMyTransfersQuery(OwnerId, null, 25), CancellationToken.None);
        Assert.NotEmpty(page1.NextCursor!); // there is a next cursor (30 > 25)

        // Page 2 via the returned cursor.
        var page2 = await handler.Handle(new ListMyTransfersQuery(OwnerId, page1.NextCursor!, 25), CancellationToken.None);

        Assert.Equal(5, page2.Items.Count);            // remaining 5 of 30 (no dupes/gaps)
        Assert.Null(page2.NextCursor);                 // last page — no next cursor
        var page1Ids = page1.Items.Select(r => r.Id).ToHashSet();
        foreach (var row in page2.Items)                // no overlap between pages
            Assert.DoesNotContain(row.Id, page1Ids);
    }

    // ---- AC-009-1 exit check: guest transfer (OwnerAppUserId=NULL) never listed for a signed-in user ----
    [Fact]
    public async Task Guest_Transfer_WithNullOwner_NotListed_For_SignedInUser()
    {
        var store = new StubTransferStore();
        for (int i = 0; i < 3; i++)
            store.Transfers.Add(new Transfer { Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, Status = 1 });

        // A guest-created transfer: OwnerAppUserId is null (TA-4.2#14 — never listed for a signed-in user).
        var guestTransfer = new Transfer { Id = Guid.NewGuid(), OwnerAppUserId = null, Status = 1 };
        store.Transfers.Add(guestTransfer);

        var handler = new ListMyTransfersHandler(store);
        var result = await handler.Handle(new ListMyTransfersQuery(OwnerId, null, 25), CancellationToken.None);

        // Exactly the 3 owned rows; the guest row is filtered out by the store contract.
        Assert.Equal(3, result.Items.Count);
        Assert.DoesNotContain(guestTransfer.Id, result.Items.Select(r => r.Id));
    }

    // Helper to retrieve a transfer's CreatedAtUtc from the stub by Id.
    private static DateTime CreatedOf(StubTransferStore store, Guid id) =>
        store.Transfers.First(t => t.Id == id).CreatedAtUtc;

    // ---- Stubs (T-008-06 style — hand-written, no Moq) ----
    private sealed class StubTransferStore : ITransferStore
    {
        public List<Transfer> Transfers { get; } = [];

        // ITransferStore members the handler doesn't need.
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<IdempotencyKey?>(null);
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);

        /// <summary>
        /// Simulates the EF store contract (TA-4.1.4): filter OwnerAppUserId == ownerId + cursor predicate,
        /// order CreatedAtUtc desc then Id desc, Take(limit+1), encode nextCursor from the last returned row.
        /// </summary>
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
        {
            var filtered = Transfers
                .Where(t => t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
                .Where(t => afterUtc is null || t.CreatedAtUtc < afterUtc.Value || (t.CreatedAtUtc == afterUtc.Value && t.Id < afterIdExcluded!.Value))
                .OrderByDescending(t => t.CreatedAtUtc)
                .ThenByDescending(t => t.Id)
                .Take(limit + 1)
                .ToList();

            var items = filtered.Take(limit).Select(t => new MyTransferRow(
                t.Id, "Transfer", t.FileCount, t.TotalBytes, t.Status, t.ExpiresAtUtc,
                t.DownloadsCount, t.MaxDownloads, 0)).ToList();
            var nextCursor = filtered.Count > limit
                ? CursorCodec.Encode(filtered[limit - 1].CreatedAtUtc, filtered[limit - 1].Id)
                : null;

            return Task.FromResult((items, nextCursor));
        }
    }
}
