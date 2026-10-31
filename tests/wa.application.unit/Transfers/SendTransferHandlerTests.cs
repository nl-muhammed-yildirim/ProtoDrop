using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-002-04: the handler is the only business logic behind POST /api/v1/transfers/{id}/send (TA-4.2a).
// Activate Draft→Active, store normalized EmailRecipient rows, emit transfer.created via outbox.
public class SendTransferHandlerTests
{
    private readonly StubSendStore _store;
    private readonly StubTransferStore _meterStore; // T-007-05: fake ITransferStore — the meter's single FR-007-4 read
    private readonly StubLimitsProvider _limits;
    private readonly StubEventPublisher _eventPublisher;
    private readonly RecordingTelemetryLogger _telemetryLogger;
    private readonly WaTelemetryReporter _telemetry;
    private readonly SendTransferHandler _handler;
    private readonly StubAppUserStore _users; // T-008-08: fake IAppUserStore — session-owner display-name resolution

    public SendTransferHandlerTests() : this(new StubLimitsProvider()) { }

    private SendTransferHandlerTests(StubLimitsProvider limits)
    {
        _store = new StubSendStore();
        _meterStore = new StubTransferStore();
        _limits = limits;
        _eventPublisher = new StubEventPublisher();
        _telemetryLogger = new RecordingTelemetryLogger();
        _telemetry = new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("test-key"));
        _users = new StubAppUserStore();
        _handler = NewHandler(limits);
    }

    private Transfer NewDraftTransfer(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        LinkId = "ABCD1234",
        Status = 0,
        TotalBytes = 100,
        FileCount = 1,
        SenderName = "Guest",
        CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_HappyPath_WithEmails_NormalizesAndStoresRecipients()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 500,
            FileCount = 2,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _handler.Handle(
            new SendTransferCommand(transferId, "Alice", "alice@example.com", "Hi", ["A@X.COM, b@y.com "]),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status); // Active
        Assert.Equal(2, result.Recipients.Count);
        Assert.Contains("a@x.com", result.Recipients);
        Assert.Contains("b@y.com", result.Recipients);

        // DB assertions.
        Assert.Equal(1, _store.Transfer!.Status);
        Assert.Equal("Alice", _store.Transfer.SenderName);
        Assert.Equal("alice@example.com", _store.Transfer.SenderEmail);
        Assert.Equal("Hi", _store.Transfer.Note);
        Assert.Equal(2, _store.Recipients.Count);
        Assert.All(_store.Recipients, r => Assert.Equal(transferId, r.TransferId));
        Assert.Contains(_store.Recipients, r => r.Address == "a@x.com");
        Assert.Contains(_store.Recipients, r => r.Address == "b@y.com");

        // Event emitted.
        Assert.Single(_eventPublisher.Published);
        var envelope = _eventPublisher.Published[0];
        Assert.Equal(EventType.TransferCreated, envelope.Type);
        Assert.Equal("ABCD1234", envelope.PartitionKey);
    }

    [Fact]
    public async Task Handle_ZeroEmails_LinkOnlyTransfer_NoRecipientRows()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "WXYZ5678",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, []),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
        Assert.Empty(result.Recipients);

        // No EmailRecipient rows.
        Assert.Empty(_store.Recipients);

        // Event emitted with empty emails.
        Assert.Single(_eventPublisher.Published);
    }

    [Fact]
    public async Task Handle_TransferNotFound_ReturnsNull()
    {
        _store.Transfer = null;

        var result = await _handler.Handle(
            new SendTransferCommand(Guid.NewGuid(), null, null, null, []),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_AlreadySent_Throws_InvalidOperationException()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 1, // Already Active
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ExceedsMaxEmails_Throws_TransferValidationException()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        // Default MaxEmails is 20 — send 21 addresses.
        var emails = Enumerable.Range(0, 21).Select(i => $"user{i}@example.com").ToList();

        var ex = await Assert.ThrowsAsync<TransferValidationException>(() =>
            _handler.Handle(new SendTransferCommand(transferId, null, null, null, emails), CancellationToken.None));

        Assert.Contains("Maximum", ex.Message);
    }

    // ---- T-007-04 / AC-007-1: send-time size re-check ----

    [Fact]
    public async Task Handle_TotalBytesExceedsMaxTransferSize_ThrowsWithBothNumbers()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _store.Transfer.TotalBytes = 5_368_709_121; // 1 byte over Free's MAX_TRANSFER_SIZE

        var ex = await Assert.ThrowsAsync<TransferSizeExceededException>(() =>
            _handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None));

        // Properties carry both numbers — the endpoint maps to 422 TRANSFER_SIZE_EXCEEDED from these.
        Assert.Equal(5_368_709_121, ex.TotalBytes);
        Assert.Equal(5_368_709_120, ex.MaxTransferSize);

        // AC-007-1 exact wording (T-007-02 canonical form): both formatted GB numbers + 'exceeds'.
        Assert.Contains("exceeds", ex.Message);
        Assert.Contains((5_368_709_121 / 1073741824.0).ToString("F1") + " GB", ex.Message);
        Assert.Contains((5_368_709_120 / 1073741824.0).ToString("F1") + " GB", ex.Message);

        // Checks precede mutation — the transfer must NOT be activated.
        Assert.Equal(0, _store.Transfer!.Status);
        Assert.Empty(_eventPublisher.Published);
    }

    [Fact]
    public async Task Handle_TotalAtExactMaxTransferSize_SendsSuccessfully()
    {
        // Inclusive boundary: exactly-at-limit passes the re-check (> is strict).
        var limits = new StubLimitsProvider { Record = new LimitsRecord
        {
            MaxEmails = 20,
            RetentionDays = 30,
            MaxTransferSize = 1000,
            MaxSingleFile = 1000
        } };
        var handler = NewHandler(limits);

        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _store.Transfer.TotalBytes = 1000; // exactly MAX_TRANSFER_SIZE

        var result = await handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status); // Active — the re-check did not trip.
    }

    [Fact]
    public async Task Handle_Exactly20Recipients_SendsSuccessfully()
    {
        // Instruction 2 boundary pin: the MAX_EMAILS cap is inclusive — exactly 20 passes on Free (MaxEmails=20 from the stub record).
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        var emails = Enumerable.Range(0, 20).Select(i => $"user{i}@example.com").ToList();
        var result = await _handler.Handle(new SendTransferCommand(transferId, null, null, null, emails), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(20, result!.Recipients.Count); // all 20 recipients stored — the cap did not trip
    }

    private SendTransferHandler NewHandler(StubLimitsProvider limits) =>
        new SendTransferHandler(_store, _meterStore, limits, _eventPublisher, _telemetry, Microsoft.Extensions.Logging.Abstractions.NullLogger<SendTransferHandler>.Instance, _users);

    // ---- T-007-05 / AC-007-2 (meter): storage_bytes_active on send ----

    [Fact]
    public async Task StorageBytesActive_EmitsRequeriedPostSaveValue_ForAccountOwner()
    {
        // Status 0→1 puts this row into the FR-007-4 SUM expression at send time — the meter is the re-run read after SaveSendAsync.
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _store.Transfer.OwnerAppUserId = owner;

        // The fake returns the post-activation sum (prior 2 GB + this transfer's 100 bytes).
        const long PostActivationSum = 2_000_000_100;
        _meterStore.ActiveStorageBytes = PostActivationSum;

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);

        // Exactly one read, for this owner — the EF expression lives in TransferStore.cs, not duplicated here.
        var calls = _meterStore.Calls;
        Assert.Single(calls);
        Assert.Equal(owner, calls[0]);
        var entry = Assert.Single(_telemetryLogger.Entries, e => e.RenderedText.StartsWith("storage_bytes_active"));
        Assert.Contains(PostActivationSum.ToString(), entry.RenderedText);
    }

    [Fact]
    public async Task StorageBytesActive_NotEmitted_WhenOwnerAppUserIdNull()
    {
        // Guest: no owner → no meter (mirrors the finalize-side EC-007-3 guard).
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);

        Assert.Empty(_telemetryLogger.Entries.Where(e => e.RenderedText.StartsWith("storage_bytes_active")));
        Assert.Empty(_meterStore.Calls);
    }

    [Fact]
    public async Task StorageBytesActive_NotEmitted_WhenTransferNotFound()
    {
        // Null early-return path — no save, no meter.
        _store.Transfer = null;

        var result = await _handler.Handle(new SendTransferCommand(Guid.NewGuid(), null, null, null, []), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(_telemetryLogger.Entries.Where(e => e.RenderedText.StartsWith("storage_bytes_active")));
    }

    [Fact]
    public async Task Handle_DuplicateEmails_Deduplicated()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, ["a@x.com", "A@X.COM", "b@y.com"]),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Recipients.Count); // deduplicated
    }

    [Fact]
    public async Task Handle_InvalidEmails_FilteredOut()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        // "not-an-email" is invalid; "valid@example.com" is valid.
        var result = await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, ["not-an-email", "valid@example.com"]),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result!.Recipients);
        Assert.Equal("valid@example.com", result.Recipients[0]);
    }

    [Fact]
    public async Task Handle_SenderNameNull_DefaultsToAnonymous()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, []),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Anonymous", _store.Transfer!.SenderName);
    }

    [Fact]
    public async Task Handle_ExpireSetFromRetentionDays()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        var before = DateTime.UtcNow.AddDays(30).AddSeconds(-5);
        var result = await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, []),
            CancellationToken.None);
        var after = DateTime.UtcNow.AddDays(30).AddSeconds(5);

        Assert.NotNull(result);
        Assert.True(result!.ExpiresAtUtc >= before && result.ExpiresAtUtc <= after);
    }

    [Fact]
    public async Task Handle_WithPassword_StoresPbkdf2Hash_NotPlaintext()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, [], "s3cret"),
            CancellationToken.None);

        var hash = _store.Transfer!.PasswordHash;
        Assert.NotNull(hash);
        Assert.NotEqual("s3cret", hash);

        // Verify the hash is a valid PBKDF2 hash for the password.
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
        var verifyResult = hasher.VerifyHashedPassword(transferId.ToString("n"), hash, "s3cret");
        Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public async Task Handle_SamePassword_DifferentTransfers_ProduceDifferentHashes()
    {
        var transferId1 = Guid.NewGuid();
        var transferId2 = Guid.NewGuid();

        // First transfer.
        _store.Transfer = new Transfer
        {
            Id = transferId1,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };
        await _handler.Handle(
            new SendTransferCommand(transferId1, null, null, null, [], "samepass"),
            CancellationToken.None);
        var hash1 = _store.Transfer!.PasswordHash;

        // Second transfer.
        _store.Transfer = new Transfer
        {
            Id = transferId2,
            LinkId = "WXYZ5678",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };
        await _handler.Handle(
            new SendTransferCommand(transferId2, null, null, null, [], "samepass"),
            CancellationToken.None);
        var hash2 = _store.Transfer!.PasswordHash;

        Assert.NotNull(hash1);
        Assert.NotNull(hash2);
        Assert.NotEqual(hash1, hash2); // per-transfer salt produces different hashes
    }

    [Fact]
    public async Task Handle_NoPassword_LeavesColumnNull()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, []),
            CancellationToken.None);

        Assert.Null(_store.Transfer!.PasswordHash);
    }

    // ---- T-008-08 / FR-008-7: send-time attribution — owner + "From" default ----

    private AppUser NewUser(Guid id, string displayName, string email) => new()
    { Id = id, Email = email, DisplayName = displayName, Theme = "system", PlanId = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };

    [Fact]
    public async Task Handle_SignedIn_OwnerSetFromSession_AndFromDefaultsToDisplayName()
    {
        // Exit (a): signed-in send → OwnerAppUserId = session user id; From = display name.
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _users.UserById[owner] = NewUser(owner, "Ada Lovelace", "ada@example.com");

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);

        Assert.Equal(owner, _store.Transfer!.OwnerAppUserId);
        Assert.Equal("Ada Lovelace", _store.Transfer.SenderName);
    }

    [Fact]
    public async Task Handle_Guest_OwnerNull_Anonymous()
    {
        // Exit (b): guest send → OwnerAppUserId = NULL; From = "Anonymous".
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);

        Assert.Null(_store.Transfer!.OwnerAppUserId);
        Assert.Equal("Anonymous", _store.Transfer.SenderName);
    }

    [Fact]
    public async Task Handle_DraftGuest_SentSignedIn_AttributesToSessionUser()
    {
        // Exit (c): draft created as a guest, sent while signed in → attributed at send time (not draft time).
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _store.Transfer.OwnerAppUserId = null; // draft was finalized as a guest
        _users.UserById[owner] = NewUser(owner, "Ada Lovelace", "ada@example.com");

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);

        Assert.Equal(owner, _store.Transfer!.OwnerAppUserId); // overwritten from null — send-time resolution
        Assert.Equal("Ada Lovelace", _store.Transfer.SenderName);
    }

    [Fact]
    public async Task Handle_OverrideWins_SenderNameBeatsDisplayName()
    {
        // Exit (d): the per-transfer From override still wins over the display-name default.
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _users.UserById[owner] = NewUser(owner, "Ada Lovelace", "ada@example.com");

        await _handler.Handle(new SendTransferCommand(transferId, "My Custom Name", null, null, [], null, owner), CancellationToken.None);

        Assert.Equal("My Custom Name", _store.Transfer!.SenderName);
        Assert.Equal(owner, _store.Transfer.OwnerAppUserId); // attribution still set
    }

    [Fact]
    public async Task Handle_EmptyDisplayName_FallsBackToLocalPart()
    {
        // T-008-06 rule: empty/blank display name → email local part (never "From: (blank)").
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _users.UserById[owner] = NewUser(owner, "  ", "ada@example.com");

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);

        Assert.Equal("ada", _store.Transfer!.SenderName);
    }

    [Fact]
    public async Task Handle_OwnerDeletedMidDraft_SignedInFromFallsBackToAnonymous()
    {
        // Edge: soft-deleted/unknown owner (GetByIdAsync → null) → "From" degrades to Anonymous;
        // attribution preserved even though the display name is gone (endpoint 13's not-found semantics).
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _users.UserById[owner] = null; // row soft-deleted or unknown

        await _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, owner), CancellationToken.None);

        Assert.Equal("Anonymous", _store.Transfer!.SenderName);
        Assert.Equal(owner, _store.Transfer.OwnerAppUserId);
    }

    // ---- Stubs ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public List<EmailRecipient> Recipients { get; } = [];

        public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Transfer is not null && Transfer.Id == id ? Transfer : null);

        public Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
        {
            Recipients.AddRange(recipients);
            return Task.CompletedTask;
        }
    }

    private sealed class StubLimitsProvider : ILimitsProvider
    {
        // T-007-04: Free values from the Part 2 frozen table — MaxTransferSize/MaxSingleFile must be set
        // (the new send-time re-check trips on 0 in every happy-path test). Tests may override per-scenario.
        public LimitsRecord Record { get; set; } = new()
        {
            MaxEmails = 20,
            RetentionDays = 30,
            MaxTransferSize = 5_368_709_120, // 5 GB (Free) — same literal FinalizeTransferHandlerTests pins for the same reason.
            MaxSingleFile = 5_368_709_120     // same as transfer size for all plans
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

    // ---- T-007-05 stubs: fake ITransferStore + telemetry capture (the new ctor dependency) ----

    private sealed class StubTransferStore : ITransferStore
    {
        public List<Guid> Calls { get; } = []; // owner ids in call order — tests pin exactly one call with the right owner
        /// <summary>What the FR-007-4 SUM expression returns — set per scenario; the handler must emit exactly this.</summary>
        public long ActiveStorageBytes { get; set; } = 0;

        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult<IdempotencyKey?>(null);

        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct)
        {
            Calls.Add(ownerId); // the meter's single read — tests pin exactly one call with the right owner
            return Task.FromResult(ActiveStorageBytes);
        }

        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(0);

        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct) =>
            Task.FromResult<(List<MyTransferRow>, string?)>(([], null)); // T-009-01: not exercised by send tests — empty page (T-009-02: signature-only).

        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) =>
            Task.FromResult<GetMyTransferResult?>(null); // T-009-03: not exercised by send tests — signature-only.

        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult<MarkDeleteResult?>(null); // T-009-04: not exercised by send tests — signature-only.
    }

    private sealed class RecordingTelemetryLogger : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(formatter(state, exception)));
        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args)
            => Entries.Add(new Entry(message ?? string.Empty));
    }

    // ---- T-008-08 stubs: fake IAppUserStore (hand-stub, NO Moq — mirrors MeProfileTests.StubAppUserStore) ----

    private sealed class StubAppUserStore : IAppUserStore
    {
        public Dictionary<Guid, AppUser?> UserById { get; } = new();

        // T-008-08 (D-3): the only member the send handler touches — null on miss OR soft-delete.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserById.TryGetValue(userId, out var user) ? user : null);

        // Members below are unused by these tests — no-op implementations keep the stub lean (IAppUserStore shape, T-008-01).
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult<AppUser?>(null);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }
}
