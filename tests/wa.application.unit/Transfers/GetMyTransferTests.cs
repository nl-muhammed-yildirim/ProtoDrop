using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

/// <summary>
/// T-009-03 (TA-4.2#15): GetMyTransferHandler — owner-scoped detail (row + files + recipients).
/// Hand-stub store only (T-008-06 / T-009-01 style); no Moq, no EF. The stub simulates the store contract:
/// Id=@id AND OwnerAppUserId=@ownerId AND DeletedAtUtc==null in ONE predicate → foreign owner / guest / deleted /
/// unknown id all return null (identical 404 body at the endpoint; no FORBIDDEN path exists).
/// </summary>
public class GetMyTransferTests
{
    private static readonly Guid OwnerId = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime BaseUtc = new(2026, 10, 30, 18, 45, 12, DateTimeKind.Utc);

    public GetMyTransferTests() { }

    // ---- Exit check 1: owner GETs one of their transfers → detail with file rows + recipient list, 200 ----
    [Fact]
    public async Task Owner_Get_ReturnsDetail_WithFilesAndRecipients()
    {
        var store = new StubTransferStore();
        var transfer = SeedOwned(store);
        var f1 = store.FileItems.First(f => f.SortOrder == 0);
        var f2 = store.FileItems.First(f => f.SortOrder == 1);

        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(transfer.Id, OwnerId), CancellationToken.None);

        // Row fields match the Transfer columns (FR-009-1: everything in a list row). Name = first file's OriginalName
        // (mirrors ListByOwnerAsync — no Name column on Transfer; TA-4.1.4 single-source-of-truth for the row payload).
        Assert.NotNull(result);
        Assert.Equal(transfer.Id, result!.Row.Id);
        Assert.Equal(f1.OriginalName, result.Row.Name);
        Assert.Equal(transfer.FileCount, result.Row.FileCount);
        Assert.Equal(transfer.TotalBytes, result.Row.TotalBytes);
        Assert.Equal(transfer.Status, result.Row.Status);
        Assert.Equal(transfer.ExpiresAtUtc, result.Row.ExpiresAtUtc);
        Assert.Equal(transfer.DownloadsCount, result.Row.DownloadsCount);
        Assert.Equal(transfer.MaxDownloads, result.Row.MaxDownloads);
        Assert.Equal(store.EmailRecipients.Count(r => r.TransferId == transfer.Id), result.Row.RecipientCount);

        // Per-file rows: ordered by SortOrder (ties broken by Id), correct name + sizeBytes.
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(f1.OriginalName, result.Files[0].Name);
        Assert.Equal(f1.SizeBytes, result.Files[0].SizeBytes);
        Assert.Equal(f2.OriginalName, result.Files[1].Name);
        Assert.Equal(f2.SizeBytes, result.Files[1].SizeBytes);

        // Recipient list: exactly the 3 addresses (EC-009-4 shape — a list, never null).
        var expected = store.EmailRecipients.Where(r => r.TransferId == transfer.Id)
            .OrderBy(r => r.Id).Select(r => r.Address).ToList();
        Assert.Equal(3, result.Recipients.Count);
        Assert.Equal(expected, result.Recipients);
    }

    // ---- Exit check 2: another signed-in user GETs it → handler null (endpoint maps to NOT_FOUND — no FORBIDDEN path) ----
    [Fact]
    public async Task Foreign_Owner_Returns_Null()
    {
        var store = new StubTransferStore();
        var transfer = SeedOwned(store);

        var foreignUserId = Guid.NewGuid();
        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(transfer.Id, foreignUserId), CancellationToken.None);

        Assert.Null(result); // the only path to 404 — no FORBIDDEN code reachable for a foreign owner
    }

    // ---- Exit check 3a: deleted row → null (same NOT_FOUND body as every other not-found shape) ----
    [Fact]
    public async Task Deleted_Row_Returns_Null()
    {
        var store = new StubTransferStore();
        var transfer = SeedOwned(store);
        transfer.DeletedAtUtc = BaseUtc; // soft-delete (TA-3.1)

        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(transfer.Id, OwnerId), CancellationToken.None);

        Assert.Null(result);
    }

    // ---- Exit check 3b: unknown id → null (identical mapping = identical body) ----
    [Fact]
    public async Task Unknown_Id_Returns_Null()
    {
        var store = new StubTransferStore();
        SeedOwned(store);

        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(Guid.NewGuid(), OwnerId), CancellationToken.None);

        Assert.Null(result);
    }

    // ---- Guest transfers (OwnerAppUserId=NULL) unreachable to ANY session user ----
    [Fact]
    public async Task Guest_Transfer_NullOwner_Returns_Null_For_SignedInUser()
    {
        var store = new StubTransferStore();
        var guestTransfer = new Transfer { Id = Guid.NewGuid(), OwnerAppUserId = null, Status = 1 };
        store.Transfers.Add(guestTransfer); // no files/recipients needed: owner predicate rejects before any projection

        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(guestTransfer.Id, OwnerId), CancellationToken.None);

        Assert.Null(result); // T-009-03: guests' transfers are unreachable here too — same NOT_FOUND body
    }

    // ---- EC-009-4 shape: link-only transfer (no files / no recipients) → empty lists, not nulls ----
    [Fact]
    public async Task LinkOnly_Transfer_EmptyFilesAndRecipients()
    {
        var store = new StubTransferStore();
        var transfer = new Transfer { Id = Guid.NewGuid(), OwnerAppUserId = OwnerId, Status = 1 };
        store.Transfers.Add(transfer); // zero FileItems, zero EmailRecipients

        var handler = new GetMyTransferHandler(store);
        var result = await handler.Handle(new GetMyTransferQuery(transfer.Id, OwnerId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result!.Files!);      // EC-009-4: empty list, not null
        Assert.Empty(result.Recipients!);  // EC-009-4: empty list, not null
    }

    // ---- Helpers ----

    private static Transfer SeedOwned(StubTransferStore store, Guid? ownerAppUserId = null) =>
        Seed(store, ownerAppUserId ?? OwnerId);

    private static Transfer Seed(StubTransferStore store, Guid ownerId)
    {
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            OwnerAppUserId = ownerId,
            Status = 1,
            FileCount = 2,
            TotalBytes = 3000,
            ExpiresAtUtc = BaseUtc.AddDays(7),
            DownloadsCount = 1,
            MaxDownloads = 5,
            CreatedAtUtc = BaseUtc
        };
        store.Transfers.Add(transfer);

        // Two FileItems with distinct SortOrder (distinct Ids via Guid.NewGuid()).
        store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transfer.Id, OriginalName = "a.txt", SizeBytes = 1000, SortOrder = 0 });
        store.FileItems.Add(new FileItem { Id = Guid.NewGuid(), TransferId = transfer.Id, OriginalName = "b.bin", SizeBytes = 2000, SortOrder = 1 });

        // Three EmailRecipients (ordered by Id in the store contract).
        for (int i = 0; i < 3; i++)
            store.EmailRecipients.Add(new EmailRecipient { Id = Guid.NewGuid(), TransferId = transfer.Id, Address = $"r{i}@example.com" });

        return transfer;
    }

    // ---- Stubs (T-008-06 style — hand-written, no Moq) ----

    private sealed class StubTransferStore : ITransferStore
    {
        public List<Transfer> Transfers { get; } = [];
        public List<FileItem> FileItems { get; } = [];
        public List<EmailRecipient> EmailRecipients { get; } = [];

        // ITransferStore members the handler doesn't need.
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<IdempotencyKey?>(null);
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);

        /// <summary>
        /// T-009-01 (TA-4.1.4) page contract — not exercised by the T-009-03 facts; empty page like the other stub files.
        /// </summary>
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) =>
            Task.FromResult<(List<MyTransferRow>, string?)>(([], null)); // T-009-03: not exercised by these facts — empty page (signature-only).

        /// <summary>
        /// T-009-04 (TA-4.2#17): signature-only for these facts — returns null = the handler's NOT_FOUND path.
        /// The full refcount behaviour is covered by DeleteTransferCommandTests' own richer stub.
        /// </summary>
        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult<MarkDeleteResult?>(null);

        /// <summary>
        /// Simulates the EF store contract (T-009-03): Id=@id AND OwnerAppUserId=@ownerId AND DeletedAtUtc==null in ONE
        /// predicate (each fact isolates its own branch); files ordered by SortOrder then Id; recipients ordered by Id.
        /// Null when no match — the handler's null = the endpoint's NOT_FOUND, never FORBIDDEN.
        /// </summary>
        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct)
        {
            var t = Transfers.FirstOrDefault(t => t.Id == id && t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null);
            if (t is null) return Task.FromResult<GetMyTransferResult?>(null);

            var files = FileItems
                .Where(f => f.TransferId == t.Id)
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Id)
                .Select(f => new GetMyTransferFile(f.OriginalName, f.SizeBytes))
                .ToList();
            var recipients = EmailRecipients
                .Where(r => r.TransferId == t.Id)
                .OrderBy(r => r.Id)
                .Select(r => r.Address)
                .ToList();

            // Mirrors ListByOwnerAsync (T-009-01): Row.Name = first file's OriginalName by SortOrder/Id or "Transfer".
            var name = files.Count > 0 ? files[0].Name : "Transfer";

            GetMyTransferResult? detail = new GetMyTransferResult(
                new MyTransferRow(t.Id, name, t.FileCount, t.TotalBytes, t.Status, t.ExpiresAtUtc, t.DownloadsCount, t.MaxDownloads, recipients.Count),
                files, recipients);
            return Task.FromResult<GetMyTransferResult?>(detail);
        }
    }
}
