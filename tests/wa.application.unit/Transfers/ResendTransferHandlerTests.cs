using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-010-01 (AC-010-1/EC-010-1/EC-010-4): ResendTransferHandler — re-send of a transfer creates a new DRAFT sharing
// the original's blobs (no data copy, ADR-009). Hand-stub stores only (no Moq — repo convention).
public class ResendTransferHandlerTests
{
    private readonly StubSendStore _store;
    private readonly StubTransferStore _transferStore;
    private readonly StubLimitsProvider _limits;

    public ResendTransferHandlerTests()
    {
        _store = new StubSendStore();
        _transferStore = new StubTransferStore();
        _limits = new StubLimitsProvider();
    }

    private ResendTransferHandler NewHandler() => new ResendTransferHandler(_store, _transferStore, _limits);

    private Transfer NewOriginalTransfer(Guid id, Guid? ownerAppUserId = null, byte status = 1) => new()
    {
        Id = id,
        LinkId = "ABCD1234",
        Status = status,
        OwnerAppUserId = ownerAppUserId,
        TotalBytes = 100,
        FileCount = 2,
        SenderName = "Alice",
        SenderEmail = "alice@example.com",
        Note = "A note",
        PasswordHash = null,
        MaxDownloads = 5,
        DownloadsCount = 2,
        ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        CreatedAtUtc = DateTime.UtcNow
    };

    // ---- AC-010-1: re-send of an expired-in-grace transfer ----

    [Fact]
    public async Task Resend_ExpiredWithinGrace_NewDraftSharesBlobRefs_RefCount2()
    {
        // Status 3 (DownloadLimit) is within the grace window — still re-sendable; no status restriction beyond excluding 4.
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();
        var blob2 = Guid.NewGuid();

        _store.Transfer = NewOriginalTransfer(originalId, ownerId, 3);
        _store.RecipientAddressesByTransferId[originalId] = ["a@x.com", "b@y.com"];
        _store.FileRowsByTransferId[originalId] = [
            new FileItemRow(Guid.NewGuid(), blob1, "a.txt", 50, "text/plain", 0),
            new FileItemRow(Guid.NewGuid(), blob2, "b.csv", 50, "text/csv", 1)
        ];
        // Each shared blob already carries its original reference (RefCount=1) — the re-send adds exactly one.
        _store.RefCountByBlobId[blob1] = 1;
        _store.RefCountByBlobId[blob2] = 1;

        var handler = NewHandler();
        var result = await handler.Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None);

        Assert.NotNull(result);
        // A genuinely new row — fresh id, fresh 8-char LinkId (no reuse), still a Draft.
        Assert.NotEqual(originalId, result!.TransferId);
        Assert.Equal(8, result.LinkId.Length);
        Assert.NotEqual("ABCD1234", result.LinkId);
        Assert.Equal(0, result.Status);
        // The new FileItem rows share BlobRefIds (same values) with fresh FileItem.Ids.
        var capture = Assert.Single(_store.ResendCaptures);
        Assert.Equal(blob1, capture.FileItems[0].BlobRefId);
        Assert.Equal(blob2, capture.FileItems[1].BlobRefId);
        Assert.NotEqual(capture.FileItems[0].Id, _store.FileRowsByTransferId[originalId][0].Id); // fresh Guids
        // RefCount 1→2 per shared blob (one re-send adds exactly one reference).
        Assert.Equal(2, _store.RefCountByBlobId[blob1]);
        Assert.Equal(2, _store.RefCountByBlobId[blob2]);
        // Original Status unchanged — the stub's original entity is unmutated by the command.
        Assert.Equal(3, _store.Transfer!.Status);
        Assert.Null(_store.Transfer.SupersededBy); // flipped only at send time via SaveSendAsync
    }

    [Fact]
    public async Task PreFill_SnapshotCopy_OriginalRowsUntouched()
    {
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();

        _store.Transfer = NewOriginalTransfer(originalId, ownerId);
        _store.Transfer.PasswordHash = "PBKDF2-SNAPSHOT";
        _store.RecipientAddressesByTransferId[originalId] = ["a@x.com"];
        _store.FileRowsByTransferId[originalId] = [new FileItemRow(Guid.NewGuid(), Guid.NewGuid(), "a.txt", 10, null, 0)];

        var result = await NewHandler().Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None);

        // FR-010-2: pre-fill snapshot — byte-for-byte copy of emails/PasswordHash/Note/SenderName/SenderEmail.
        Assert.NotNull(result);
        Assert.Equal(["a@x.com"], result!.Recipients);
        Assert.Equal("A note", result.Note);
        Assert.Equal("Alice", result.SenderName);
        Assert.Equal("alice@example.com", result.SenderEmail);
        Assert.True(result.PasswordRequired); // PBKDF2 snapshot copied → the draft is still gated

        // The ORIGINAL row is untouched — assert on the stub's stored entity.
        Assert.Equal("PBKDF2-SNAPSHOT", _store.Transfer!.PasswordHash);
        Assert.Null(_store.Transfer.SupersededBy);
        Assert.Null(_store.Transfer.SourcedFrom); // only the new draft carries SourcedFrom
    }

    // ---- EC-010-1: linear chain, RefCount 2→3, no cycles ----

    [Fact]
    public async Task ResendOfResend_RefCount3_LinearChain_NoCycles()
    {
        var ownerId = Guid.NewGuid();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        // A is the original (SourcedFrom null). B was re-sent from A (SourcedFrom = A).
        _store.Transfer = NewOriginalTransfer(idA, ownerId);
        _store.SourcedFromByTransferId[idA] = null;
        _store.RecipientAddressesByTransferId[idA] = ["a@x.com"];
        _store.FileRowsByTransferId[idA] = [new FileItemRow(Guid.NewGuid(), Guid.NewGuid(), "a.txt", 10, null, 0)];
        // Seed the original blob's reference (RefCount=1) — A already references it.
        _store.RefCountByBlobId[_store.FileRowsByTransferId[idA][0].BlobRefId] = 1;
        var b = await NewHandler().Handle(new ResendTransferCommand(idA, ownerId), CancellationToken.None);
        Assert.NotNull(b);
        Assert.Equal(idA, _store.LastDraft!.SourcedFrom); // B.SourcedFrom = A (linear chain)

        // C is re-sent from B — RefCount 2→3; linear chain (C.SourcedFrom = B), no cycles.
        var originalB = NewOriginalTransfer(idB, ownerId);
        originalB.LinkId = b!.LinkId; // B's linkId — the re-send command loads by Id anyway
        _store.Transfer = originalB;
        _store.SourcedFromByTransferId[idB] = idA; // B.SourcedFrom = A (chain)
        _store.RecipientAddressesByTransferId[idB] = ["a@x.com"];
        var blobId = _store.FileRowsByTransferId[idA][0].BlobRefId;
        _store.FileRowsByTransferId[idB] = [new FileItemRow(Guid.NewGuid(), blobId, "a.txt", 10, null, 0)];

        var c = await NewHandler().Handle(new ResendTransferCommand(idB, ownerId), CancellationToken.None);

        Assert.NotNull(c);
        Assert.Equal(3, _store.RefCountByBlobId[blobId]); // A→B: 2; B→C: 3 (linear chain)
        Assert.Equal(idB, _store.LastDraft.SourcedFrom); // C.SourcedFrom = B — no cycle back to A
    }

    // ---- EC-010-4 / no enumeration ----

    [Fact]
    public async Task GuestOwner_ResendForbiddenExceptionThrown()
    {
        var originalId = Guid.NewGuid();
        _store.Transfer = NewOriginalTransfer(originalId, null); // guest row — no owner

        await Assert.ThrowsAsync<ResendForbiddenException>(() =>
            NewHandler().Handle(new ResendTransferCommand(originalId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task ForeignOwner_NullResult_NotFoundPath()
    {
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();
        _store.Transfer = NewOriginalTransfer(Guid.NewGuid(), ownerId);

        var result = await NewHandler().Handle(new ResendTransferCommand(_store.Transfer!.Id, otherOwnerId), CancellationToken.None);

        Assert.Null(result); // foreign owner → null result — endpoint maps to NOT_FOUND (no enumeration)
    }

    [Fact]
    public async Task Deleted_Transfer_NotFoundPath()
    {
        var ownerId = Guid.NewGuid();
        _store.Transfer = NewOriginalTransfer(Guid.NewGuid(), ownerId, 4); // Status=4 → no re-send

        var result = await NewHandler().Handle(new ResendTransferCommand(_store.Transfer!.Id, ownerId), CancellationToken.None);

        Assert.Null(result); // Status=4 → null result — endpoint maps to NOT_FOUND
    }

    [Fact]
    public async Task Unknown_Transfer_NotFoundPath()
    {
        _store.Transfer = null;

        var result = await NewHandler().Handle(new ResendTransferCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result); // not found → null result — endpoint maps to NOT_FOUND
    }

    // ---- New draft field shape: MaxDownloads=1, DownloadsCount=0, ExpiresAtUtc from limits ----

    [Fact]
    public async Task NewDraft_MaxDownloads1_DownloadsCount0_ExpiresFromLimits()
    {
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        _store.Transfer = NewOriginalTransfer(originalId, ownerId);
        _limits.Record.MaxDownloads = 99; // the draft must NOT copy MaxDownloads — it's always 1 for a re-send.
        _store.FileRowsByTransferId[originalId] = [new FileItemRow(Guid.NewGuid(), Guid.NewGuid(), "a.txt", 10, null, 0)];

        var before = DateTime.UtcNow.AddDays(30).AddSeconds(-5);
        var result = await NewHandler().Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None);
        var after = DateTime.UtcNow.AddDays(30).AddSeconds(5);

        Assert.NotNull(result);
        var draft = _store.LastDraft;
        Assert.Equal(1, draft!.MaxDownloads);
        Assert.Equal(0, draft.DownloadsCount);
        Assert.True(draft.ExpiresAtUtc >= before && draft.ExpiresAtUtc <= after); // RETENTION_DAYS via limits (never a literal)
    }

    // ---- Stubs (no Moq — mirrors SendTransferHandlerTests conventions) ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public List<EmailRecipient> Recipients { get; } = [];
        public Dictionary<Guid, string[]> RecipientAddressesByTransferId { get; } = [];
        public Dictionary<Guid, FileItemRow[]> FileRowsByTransferId { get; } = [];
        public Dictionary<Guid, Guid?> SourcedFromByTransferId { get; } = [];
        public Dictionary<Guid, int> RefCountByBlobId { get; } = new();
        public List<(Transfer Draft, IReadOnlyList<FileItem> FileItems, IReadOnlyList<BlobRef> SharedBlobRefs)> ResendCaptures { get; } = [];
        public Transfer? LastDraft => ResendCaptures.Count > 0 ? ResendCaptures[^1].Draft : null;

        public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Transfer is not null && Transfer.Id == id ? Transfer : null);

        public Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
        {
            Recipients.AddRange(recipients);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult(RecipientAddressesByTransferId.TryGetValue(transferId, out var a) ? (IReadOnlyList<string>)a : (IReadOnlyList<string>)[]);

        public Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult(FileRowsByTransferId.TryGetValue(transferId, out var f) ? (IReadOnlyList<FileItemRow>)f : (IReadOnlyList<FileItemRow>)[]);

        public Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct)
        {
            ResendCaptures.Add((draft, fileItems, sharedBlobRefs));
            foreach (var blobRef in sharedBlobRefs)
                RefCountByBlobId[blobRef.Id] = RefCountByBlobId.GetValueOrDefault(blobRef.Id) + 1; // RefCount++ per shared blob
            return Task.CompletedTask;
        }
    }

    private sealed class StubTransferStore : ITransferStore
    {
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult<IdempotencyKey?>(null);

        // LinkId mint collision check — never a collision in these tests (once-and-retry passes on the first mint).
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);

        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);

        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);

        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) =>
            Task.FromResult<(List<MyTransferRow>, string?)>(([], null));

        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) =>
            Task.FromResult<GetMyTransferResult?>(null);

        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult<MarkDeleteResult?>(null);
    }

    private sealed class StubLimitsProvider : ILimitsProvider
    {
        // Free values from the Part 2 frozen table — same convention as SendTransferHandlerTests.
        public LimitsRecord Record { get; set; } = new()
        {
            MaxEmails = 20,
            RetentionDays = 30,
            MaxTransferSize = 5_368_709_120, // 5 GB (Free)
            MaxSingleFile = 5_368_709_120
        };

        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record);
    }
}
