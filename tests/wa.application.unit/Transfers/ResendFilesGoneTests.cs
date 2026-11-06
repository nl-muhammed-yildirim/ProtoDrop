using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-010-02 (AC-010-2 / exit check c): the FILES_GONE gate at creation time. The StubSendStore mirrors the REAL gate
// semantics exactly: in SaveResendAsync it throws FilesGoneException when any passed blob-ref id is missing from its
// seeded rows or its BlobPath is not in the private StubBlobStore's existing set — before recording anything;
// otherwise it records the draft/fileItems/recipients and RefCount bumps. No Moq (repo convention).
public class ResendFilesGoneTests
{
    private readonly StubSendStore _store;
    private readonly StubTransferStore _transferStore;
    private readonly StubLimitsProvider _limits;

    public ResendFilesGoneTests()
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

    // ---- AC-010-2: blob-ref row missing (f-delete-blobs removed both the blob and its BlobRef row) → gate fires ----

    [Fact]
    public async Task Resend_BlobRefRowMissing_GateFires_ThrowsFilesGone_ZeroRowsNoRefCount()
    {
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();
        var blob2 = Guid.NewGuid();

        _store.Transfer = NewOriginalTransfer(originalId, ownerId, 1);
        _store.RecipientAddressesByTransferId[originalId] = ["a@x.com"];
        // The FileItem rows still point at the blob ids — but the BlobRef row for blob2 was removed by f-delete-blobs.
        _store.FileRowsByTransferId[originalId] = [
            new FileItemRow(Guid.NewGuid(), blob1, "a.txt", 50, "text/plain", 0),
            new FileItemRow(Guid.NewGuid(), blob2, "b.csv", 50, "text/csv", 1)
        ];
        _store.BlobRefRowsByBlobId[blob1] = new BlobRef { Id = blob1, BlobPath = "transfers/blob1.dat", RefCount = 1 };
        // blob2's row is GONE — f-delete-blobs deleted blob + row in one pass (T-005-03 semantics).
        // blob1's path is physically present (so its check passes); blob2 throws via the missing-row branch.
        _store.BlobStore.ExistingPaths.Add("transfers/blob1.dat");

        await Assert.ThrowsAsync<FilesGoneException>(() =>
            NewHandler().Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None));

        // AC-010-2: zero new rows — the stub recorded nothing (the throw happens in step 0, before any insert).
        Assert.Empty(_store.ResendCaptures);
        Assert.Empty(_store.Recipients);
        // No RefCount change — zero bumps were recorded when the gate fired.
        Assert.Empty(_store.RefCountByBlobId);
    }

    [Fact]
    public async Task Resend_BlobNotInStorage_GateFires_ThrowsFilesGone_ZeroRows()
    {
        // The BlobRef row is present but the blob is physically gone — ExistsAsync false → gate fires.
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();

        _store.Transfer = NewOriginalTransfer(originalId, ownerId, 1);
        _store.RecipientAddressesByTransferId[originalId] = ["a@x.com"];
        _store.FileRowsByTransferId[originalId] = [new FileItemRow(Guid.NewGuid(), blob1, "a.txt", 50, "text/plain", 0)];
        var blobRefRow = new BlobRef { Id = blob1, BlobPath = "transfers/blob1.dat", RefCount = 1 };
        _store.BlobRefRowsByBlobId[blob1] = blobRefRow;
        // The blob row is present but NOT in storage — ExistsAsync(blobPath) returns false → gate fires.
        await Assert.ThrowsAsync<FilesGoneException>(() =>
            NewHandler().Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None));

        Assert.Empty(_store.ResendCaptures); // zero new rows (AC-010-2 — no half-created draft remains)
        Assert.Equal(1, blobRefRow.RefCount); // no RefCount change when the gate fires
    }

    // ---- Exit check c: blob exists but PhysicallyDeletedAtUtc set (T-005-03 buffer window) → ALLOWED ----

    [Fact]
    public async Task BufferWindow_PhysicallyDeletedAtUtcSet_BlobStillExists_ResendSucceeds()
    {
        // The flag is set (24 h buffer window, T-005-03 output) but the blob is still physically present — the gate
        // checks physical deletion via ExistsAsync, NOT the flag alone → allowed.
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();

        _store.Transfer = NewOriginalTransfer(originalId, ownerId, 1);
        _store.RecipientAddressesByTransferId[originalId] = ["a@x.com"];
        _store.FileRowsByTransferId[originalId] = [new FileItemRow(Guid.NewGuid(), blob1, "a.txt", 50, "text/plain", 0)];
        var blobRefRow = new BlobRef
        {
            Id = blob1,
            BlobPath = "transfers/blob1.dat",
            RefCount = 1,
            PhysicallyDeletedAtUtc = DateTime.UtcNow.AddHours(24) // buffer window — the gate must NOT read this
        };
        _store.BlobRefRowsByBlobId[blob1] = blobRefRow;
        // The blob is physically PRESENT — the gate's ExistsAsync check passes despite the flag (exit check c).
        _store.BlobStore.ExistingPaths.Add("transfers/blob1.dat");
        // Seed the original reference (RefCount=1) — the re-send adds exactly one.
        _store.RefCountByBlobId[blob1] = 1;

        var result = await NewHandler().Handle(new ResendTransferCommand(originalId, ownerId), CancellationToken.None);

        Assert.NotNull(result); // the re-send is ALLOWED (exit check c)
        Assert.Equal(0, result!.Status); // still a Draft
        // RefCount 1→2 — one re-send adds exactly one reference.
        Assert.Equal(2, _store.RefCountByBlobId[blob1]);
    }

    // ---- Regression guard: T-010-01's behavior paths are untouched by this task (one smoke fact is enough) ----

    [Fact]
    public async Task GuestOwner_ResendStillForbidden_GateNeverRuns()
    {
        var originalId = Guid.NewGuid();
        _store.Transfer = NewOriginalTransfer(originalId, null); // guest row — no owner

        // EC-010-4 still fires before any blob check — the gate is downstream of the ownership checks.
        await Assert.ThrowsAsync<ResendForbiddenException>(() =>
            NewHandler().Handle(new ResendTransferCommand(originalId, Guid.NewGuid()), CancellationToken.None));

        Assert.Empty(_store.ResendCaptures);
    }

    // ---- Stubs (no Moq — mirrors ResendTransferHandlerTests conventions; the gate is mirrored in StubSendStore) ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public List<EmailRecipient> Recipients { get; } = [];
        public Dictionary<Guid, string[]> RecipientAddressesByTransferId { get; } = [];
        public Dictionary<Guid, FileItemRow[]> FileRowsByTransferId { get; } = [];
        public Dictionary<Guid, BlobRef> BlobRefRowsByBlobId { get; } = new(); // the "database" of blob rows (T-005-03 output)
        public Dictionary<Guid, int> RefCountByBlobId { get; } = new(); // recorded RefCount++ bumps (asserted: zero when the gate fires)
        public List<(Transfer Draft, IReadOnlyList<FileItem> FileItems, IReadOnlyList<EmailRecipient> Recipients, IReadOnlyList<BlobRef> SharedBlobRefs)> ResendCaptures { get; } = [];

        private readonly StubBlobStore _blobStore = new(); // the physical-storage surface (ExistsAsync only)
        public StubBlobStore BlobStore => _blobStore; // seed ExistingPaths per fact (physical presence)

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

        public Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct)
        {
            // T-010-02: the REAL gate semantics — step 0, before recording anything. Missing row OR ExistsAsync false → throw.
            foreach (var blobRefId in sharedBlobRefs.Select(b => b.Id).Distinct())
            {
                if (!BlobRefRowsByBlobId.TryGetValue(blobRefId, out var row))
                    throw new FilesGoneException(); // missing row = gone (f-delete-blobs removed blob and row together)

                var exists = _blobStore.ExistingPaths.Contains(row.BlobPath);
                if (!exists)
                    throw new FilesGoneException(); // blob physically gone
            }
            ResendCaptures.Add((draft, fileItems, recipients, sharedBlobRefs));
            foreach (var blobRefId in sharedBlobRefs.Select(b => b.Id).Distinct())
                RefCountByBlobId[blobRefId] = RefCountByBlobId.GetValueOrDefault(blobRefId) + 1; // RefCount++ per shared blob
            return Task.CompletedTask;
        }
    }

    // ---- The blob store stub: physical-presence set (the gate's only primitive, mirroring IBlobStore.ExistsAsync) ----

    private sealed class StubBlobStore : IBlobStore
    {
        public HashSet<string> ExistingPaths { get; } = new();

        public Task UploadAsync(string path, Stream content, CancellationToken ct = default)
        {
            ExistingPaths.Add(path);
            return Task.CompletedTask;
        }

        // Finalize no longer buffers blobs: these mirror the server-side copy + metadata size read.
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> GetSizeAsync(string path, CancellationToken ct = default) => Task.FromResult(0L);

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default) =>
            throw new NotSupportedException("Only ExistsAsync is exercised by this suite.");

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(ExistingPaths.Contains(path));

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            ExistingPaths.Remove(path);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTransferStore : ITransferStore
    {
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<IdempotencyKey?>(null);
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) => Task.FromResult<(List<MyTransferRow>, string?)>(([], null));
        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) => Task.FromResult<GetMyTransferResult?>(null);
        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult<MarkDeleteResult?>(null);
    }

    private sealed class StubLimitsProvider : ILimitsProvider
    {
        public LimitsRecord Record { get; set; } = new()
        {
            MaxEmails = 20,
            RetentionDays = 30,
            MaxTransferSize = 5_368_709_120, // 5 GB (Free) — same literal FinalizeTransferHandlerTests pins
            MaxSingleFile = 5_368_709_120
        };

        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record);
    }
}
