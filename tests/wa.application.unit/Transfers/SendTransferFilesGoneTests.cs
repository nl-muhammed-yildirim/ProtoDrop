using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-010-02 (EC-010-2): the FILES_GONE gate at SEND time — a pending re-send draft whose original was deleted by its
// owner is sent through the existing send path, SaveSendAsync's gate fires, and the exception propagates through
// SendTransferHandler to /send. Same hand-stub conventions as ResendFilesGoneTests.cs (no Moq). The StubSendStore
// mirrors the REAL gate semantics: in SaveSendAsync it runs the same gate against this transfer's seeded file items
// ONLY when SourcedFrom is set, before recording anything.
public class SendTransferFilesGoneTests
{
    private readonly StubSendStore _store;
    private readonly StubAppUserStore _users;
    private readonly StubEventPublisher _eventPublisher;

    public SendTransferFilesGoneTests()
    {
        _store = new StubSendStore();
        _users = new StubAppUserStore();
        _eventPublisher = new StubEventPublisher();
    }

    private Transfer NewDraft(Guid id, Guid? sourcedFrom) => new()
    {
        Id = id,
        LinkId = "ABCD1234",
        Status = 0,
        TotalBytes = 100,
        FileCount = 1,
        SenderName = "Guest",
        CreatedAtUtc = DateTime.UtcNow,
        SourcedFrom = sourcedFrom
    };

    private SendTransferHandler NewHandler()
    {
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, System.Text.Encoding.UTF8.GetBytes("test-key"));
        return new SendTransferHandler(
            _store,
            new StubTransferStore(),
            new StubLimitsProvider(),
            _eventPublisher,
            telemetry,
            NullLogger<SendTransferHandler>.Instance,
            _users);
    }

    // ---- EC-010-2: blob row missing (f-delete-blobs removed it) → gate fires at send time → no orphan rows ----

    [Fact]
    public async Task Send_ResendDraft_BlobRowMissing_GateFires_ThrowsFilesGone_ZeroRows()
    {
        var originalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();

        // The pending re-send draft (SourcedFrom = original.Id, Status=0) whose FileItem rows point at a BlobRef no longer present.
        _store.Transfer = NewDraft(draftId, originalId);
        // No BlobRef row for blob1 — f-delete-blobs removed blob + row in one pass (T-005-03 semantics).
        _store.FileItemsByTransferId[draftId] = [new FileItem { Id = Guid.NewGuid(), TransferId = draftId, BlobRefId = blob1 }];

        await Assert.ThrowsAsync<FilesGoneException>(() =>
            NewHandler().Handle(new SendTransferCommand(draftId, null, null, null, ["a@x.com"]), CancellationToken.None));

        // No orphan rows: zero status flips, no SupersededBy write, no EmailRecipient writes, no transfer.created.
        Assert.Empty(_store.StatusFlips);
        Assert.Empty(_store.SupersededByWrites);
        Assert.Empty(_store.Recipients);
        Assert.Empty(_eventPublisher.Published);
    }

    [Fact]
    public async Task Send_ResendDraft_BlobNotInStorage_ThrowsFilesGone()
    {
        // The BlobRef row is present but the blob is physically gone — ExistsAsync false → gate fires at send time.
        var originalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();

        _store.Transfer = NewDraft(draftId, originalId);
        _store.FileItemsByTransferId[draftId] = [new FileItem { Id = Guid.NewGuid(), TransferId = draftId, BlobRefId = blob1 }];
        _store.BlobRefRowsByBlobId[blob1] = new BlobRef { Id = blob1, BlobPath = "transfers/blob1.dat", RefCount = 0 };

        await Assert.ThrowsAsync<FilesGoneException>(() =>
            NewHandler().Handle(new SendTransferCommand(draftId, null, null, null, ["a@x.com"]), CancellationToken.None));

        Assert.Empty(_store.StatusFlips);
        Assert.Empty(_store.SupersededByWrites);
        Assert.Empty(_store.Recipients);
        Assert.Empty(_eventPublisher.Published);
    }

    // ---- Exit check c: buffer window (PhysicallyDeletedAtUtc set) + blob still present → send succeeds ----

    [Fact]
    public async Task BufferWindow_FlagSet_BlobStillPresent_SendSucceeds()
    {
        var originalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var blob1 = Guid.NewGuid();

        _store.Transfer = NewDraft(draftId, originalId);
        _store.FileItemsByTransferId[draftId] = [new FileItem { Id = Guid.NewGuid(), TransferId = draftId, BlobRefId = blob1 }];
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

        var result = await NewHandler().Handle(new SendTransferCommand(draftId, null, null, null, ["a@x.com"]), CancellationToken.None);

        Assert.NotNull(result); // the send is ALLOWED (exit check c — mirror of ResendFilesGoneTests' buffer-window fact)
        Assert.Equal(1, result!.Status); // Active — the draft was sent as usual
        Assert.Single(_store.StatusFlips); // recorded AFTER the gate passed
        Assert.Single(_eventPublisher.Published); // transfer.created still emitted
    }

    // ---- Normal drafts (SourcedFrom null) are untouched: the gate never runs, zero ExistsAsync calls ----

    [Fact]
    public async Task Send_NormalDraft_NoFileRows_GateNeverRuns_ZeroExistsCalls()
    {
        var draftId = Guid.NewGuid();
        _store.Transfer = NewDraft(draftId, null); // SourcedFrom = null (normal draft, no file rows)

        var result = await NewHandler().Handle(new SendTransferCommand(draftId, null, null, null, []), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status); // the send succeeded as before — /send behavior for normal drafts is untouched
        // The gate is scoped to SourcedFrom drafts only — the blob store was never consulted.
        Assert.Equal(0, _store.BlobStore.ExistsCalls);
    }

    // ---- Stubs (no Moq — mirrors SendTransferSupersedeTests conventions; the gate is mirrored in StubSendStore) ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public List<EmailRecipient> Recipients { get; } = [];
        public List<Guid> StatusFlips { get; } = []; // recorded ONLY after the gate passes (the real store's commit point)
        public List<(Guid OriginalId, Guid DraftId)> SupersededByWrites { get; } = [];
        public Dictionary<Guid, FileItem[]> FileItemsByTransferId { get; } = []; // this transfer's FileItem rows
        public Dictionary<Guid, BlobRef> BlobRefRowsByBlobId { get; } = new(); // the "database" of blob rows (T-005-03 output)

        private readonly StubBlobStore _blobStore = new(); // the physical-storage surface (ExistsAsync only)
        public StubBlobStore BlobStore => _blobStore; // seed ExistingPaths per fact + assert zero calls for normal drafts

        public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Transfer is not null && Transfer.Id == id ? Transfer : null);

        public Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
        {
            // T-010-02 (row 6): the REAL gate semantics — re-send drafts only, BEFORE any recording. A SourcedFrom draft's
            // FileItem rows point at blobs; missing row OR ExistsAsync false → throw (the transaction rolls back).
            if (transfer.SourcedFrom is not null)
            {
                var blobIds = (FileItemsByTransferId.TryGetValue(transfer.Id, out var fileItems) ? fileItems : Array.Empty<FileItem>())
                    .Select(f => f.BlobRefId)
                    .Distinct()
                    .ToList();
                foreach (var blobId in blobIds)
                {
                    if (!BlobRefRowsByBlobId.TryGetValue(blobId, out var row))
                        throw new FilesGoneException(); // missing row = gone (f-delete-blobs removed blob and row together)

                    var exists = _blobStore.ExistingPaths.Contains(row.BlobPath);
                    if (!exists)
                        throw new FilesGoneException(); // blob physically gone
                }
            }
            StatusFlips.Add(transfer.Id); // the "commit" — only reached when the gate passed
            if (transfer.SourcedFrom is not null && FileItemsByTransferId.TryGetValue(transfer.SourcedFrom.Value, out _))
                SupersededByWrites.Add((transfer.SourcedFrom.Value, transfer.Id)); // the original row still exists → SupersededBy flipped
            Recipients.AddRange(recipients);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)[]);

        public Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<FileItemRow>)[]);

        public Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct) =>
            Task.CompletedTask;
    }

    // ---- The blob store stub: physical-presence set + call counter (the gate's only primitive, mirroring IBlobStore.ExistsAsync) ----

    private sealed class StubBlobStore : IBlobStore
    {
        public HashSet<string> ExistingPaths { get; } = new();
        public int ExistsCalls { get; private set; }

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

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            ExistsCalls++;
            return Task.FromResult(ExistingPaths.Contains(path));
        }

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

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    private sealed class StubAppUserStore : IAppUserStore
    {
        public Dictionary<Guid, AppUser?> UserById { get; } = new();

        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserById.TryGetValue(userId, out var user) ? user : null);

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }
}
