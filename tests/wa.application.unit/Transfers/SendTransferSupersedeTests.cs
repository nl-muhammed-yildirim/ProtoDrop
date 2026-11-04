using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-010-01 (rows 3–4): when a re-send draft (SourcedFrom set) is sent through the existing send path,
// the original row's SupersededBy is flipped in the same transaction as Status 0→1 — and transfer.created
// is still emitted via the outbox. A normal draft (SourcedFrom null) must not touch any SupersededBy.
public class SendTransferSupersedeTests
{
    private readonly StubSendStore _store;
    private readonly StubAppUserStore _users;
    private readonly StubEventPublisher _eventPublisher;

    public SendTransferSupersedeTests()
    {
        _store = new StubSendStore();
        _users = new StubAppUserStore();
        _eventPublisher = new StubEventPublisher();
    }

    private Transfer NewDraftWithSource(Guid id, Guid? sourcedFrom, Guid? owner = null) => new()
    {
        Id = id,
        LinkId = "ABCD1234",
        Status = 0,
        TotalBytes = 100,
        FileCount = 1,
        SenderName = "Guest",
        CreatedAtUtc = DateTime.UtcNow,
        SourcedFrom = sourcedFrom,
        OwnerAppUserId = owner
    };

    [Fact]
    public async Task Send_ResendDraft_SetsSupersededByOnOriginal_TransferCreatedEmitted()
    {
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        // The original row (the one whose SupersededBy must be set when this draft is sent).
        _store.TransferById[originalId] = new Transfer
        {
            Id = originalId,
            LinkId = "ORIG1234",
            Status = 1, // Active — re-send is allowed from an active transfer (two live links, same files)
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        // The re-send draft carries SourcedFrom = original.Id.
        _store.Transfer = NewDraftWithSource(draftId, originalId, ownerId);

        var handler = NewHandler();
        var result = await handler.Handle(new SendTransferCommand(draftId, null, null, null, []), CancellationToken.None);

        // The send succeeded — the draft is now Active.
        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);

        // The original row's SupersededBy == the new transfer id (the guarded update ran with rowsAffected 1).
        Assert.Equal(draftId, _store.TransferById[originalId].SupersededBy);

        // The normal send path still emits transfer.created via IEventPublisher (existing assertion style).
        var envelope = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.TransferCreated, envelope.Type);
        Assert.Equal("ABCD1234", envelope.PartitionKey); // the DRAFT's linkId (the newly sent row)
    }

    [Fact]
    public async Task Send_NormalDraft_NoSupersededBump()
    {
        // A normal draft has SourcedFrom null — the send must not write SupersededBy on any row.
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();

        // Seed a transfer that is NOT referenced by SourcedFrom — it must stay untouched.
        _store.TransferById[originalId] = new Transfer
        {
            Id = originalId,
            LinkId = "ORIG1234",
            Status = 1,
            TotalBytes = 50,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var draftId = Guid.NewGuid();
        _store.Transfer = NewDraftWithSource(draftId, null, ownerId); // SourcedFrom = null (normal draft)

        var handler = NewHandler();
        var result = await handler.Handle(new SendTransferCommand(draftId, null, null, null, []), CancellationToken.None);

        Assert.NotNull(result);
        // No SupersededBy write — the unrelated row's SupersededBy is still null.
        Assert.Null(_store.TransferById[originalId].SupersededBy);
        // The draft itself was sent as usual.
        Assert.Equal(1, result!.Status);
    }

    [Fact]
    public async Task Send_OriginalDeletedWhileDraftSitsOpen_SupersededByNotWritten_NoError()
    {
        // rowsAffected 0 → original was deleted while the draft sat open — no error here; just no SupersededBy write.
        var ownerId = Guid.NewGuid();
        var originalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        // The original row is NOT seeded — FindByIdAsync only loads the DRAFT (the command's target).
        _store.Transfer = NewDraftWithSource(draftId, originalId, ownerId);

        var handler = NewHandler();
        var result = await handler.Handle(new SendTransferCommand(draftId, null, null, null, []), CancellationToken.None);

        // The send still succeeds (the draft row is found).
        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
        // No SupersededBy write — the original row was deleted before the send.
        Assert.False(_store.TransferById.ContainsKey(originalId)); // no original row to have SupersededBy set on it
    }

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

    // ---- Stubs (no Moq — mirrors SendTransferHandlerTests conventions) ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public Dictionary<Guid, Transfer> TransferById { get; } = [];
        public List<EmailRecipient> Recipients { get; } = [];
        // T-010-02 (row 7): the gate's inputs — this transfer's seeded file items + the blob rows (T-005-03 output) + the physical-presence set.
        public Dictionary<Guid, FileItem[]> FileItemsByTransferId { get; } = [];
        public Dictionary<Guid, BlobRef> BlobRefRowsByBlobId { get; } = new();

        private readonly StubBlobStore _blobStore = new(); // the physical-storage surface (ExistsAsync only)

        public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Transfer is not null && Transfer.Id == id ? Transfer : null);

        public Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
        {
            // T-010-02 (row 7): mirror the real gate — re-send drafts only. Look up this transfer's seeded file items and
            // throw FilesGoneException before recording if any blob is missing — exactly like row 5's stub. No existing
            // fact seeds a missing blob for a SourcedFrom draft (they seed drafts with no file rows), so the empty
            // collection early-return keeps them green (AC-010-2).
            if (transfer.SourcedFrom is not null)
            {
                var fileItems = FileItemsByTransferId.TryGetValue(transfer.Id, out var fi) ? fi : Array.Empty<FileItem>();
                foreach (var blobId in fileItems.Select(f => f.BlobRefId).Distinct())
                {
                    if (!BlobRefRowsByBlobId.TryGetValue(blobId, out var row))
                        throw new FilesGoneException(); // missing row = gone (f-delete-blobs removed blob and row together)

                    if (!_blobStore.ExistingPaths.Contains(row.BlobPath))
                        throw new FilesGoneException(); // blob physically gone
                }
            }
            // T-010-01: when SourcedFrom is set, flip SupersededBy on the original row — only if the original still exists.
            if (transfer.SourcedFrom is not null && TransferById.TryGetValue(transfer.SourcedFrom.Value, out var original))
                original.SupersededBy = transfer.Id;
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

    // T-010-02 (row 7): the blob store stub — physical-presence set (the gate's only primitive, mirroring IBlobStore.ExistsAsync).
    private sealed class StubBlobStore
    {
        public HashSet<string> ExistingPaths { get; } = new();

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(ExistingPaths.Contains(path));
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
