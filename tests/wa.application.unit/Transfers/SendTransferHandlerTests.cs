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
        var limits = new StubLimitsProvider
        {
            Record = new LimitsRecord
            {
                MaxEmails = 20,
                RetentionDays = 30,
                MaxTransferSize = 1000,
                MaxSingleFile = 1000
            }
        };
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
        new SendTransferHandler(_store, _meterStore, limits, _eventPublisher, _telemetry, Microsoft.Extensions.Logging.Abstractions.NullLogger<SendTransferHandler>.Instance, _users, TimeProvider.System);

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

        Assert.DoesNotContain(_telemetryLogger.Entries, e => e.RenderedText.StartsWith("storage_bytes_active"));
        Assert.Empty(_meterStore.Calls);
    }

    [Fact]
    public async Task StorageBytesActive_NotEmitted_WhenTransferNotFound()
    {
        // Null early-return path — no save, no meter.
        _store.Transfer = null;

        var result = await _handler.Handle(new SendTransferCommand(Guid.NewGuid(), null, null, null, []), CancellationToken.None);

        Assert.Null(result);
        Assert.DoesNotContain(_telemetryLogger.Entries, e => e.RenderedText.StartsWith("storage_bytes_active"));
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

    private static LimitsRecord SendLimits(int retentionDays, int maxDownloads) => new()
    {
        MaxEmails = 20,
        RetentionDays = retentionDays,
        MaxDownloads = maxDownloads,
        MaxTransferSize = 5_368_709_120,
        MaxSingleFile = 5_368_709_120
    };

    [Fact]
    public async Task Handle_StampsMaxDownloadsFromResolvedLimits() // T-031-04: cap comes from the plan at send, not the finalize placeholder.
    {
        _store.Transfer = NewDraftTransfer();
        _store.Transfer.MaxDownloads = 1;
        _limits.Record = SendLimits(30, 100);

        await _handler.Handle(new SendTransferCommand(_store.Transfer.Id, null, null, null, []), CancellationToken.None);

        Assert.Equal(100, _store.Transfer.MaxDownloads);
    }

    [Fact]
    public async Task Handle_PlanChangedBetweenSends_ActiveKeepsSendTimeLimits_NextSendUsesNew() // AC-018-3, US-018-04 AC block 1.
    {
        _limits.Record = SendLimits(30, 1000);
        var a = NewDraftTransfer();
        _store.Transfer = a;
        await _handler.Handle(new SendTransferCommand(a.Id, null, null, null, []), CancellationToken.None);
        var aExpiry = a.ExpiresAtUtc;

        _limits.Record = SendLimits(7, 100);
        var b = NewDraftTransfer();
        _store.Transfer = b;
        await _handler.Handle(new SendTransferCommand(b.Id, null, null, null, []), CancellationToken.None);

        Assert.Equal(aExpiry, a.ExpiresAtUtc);
        Assert.Equal(1000, a.MaxDownloads);
        Assert.InRange(b.ExpiresAtUtc, DateTime.UtcNow.AddDays(7).AddMinutes(-1), DateTime.UtcNow.AddDays(7).AddMinutes(1));
        Assert.Equal(100, b.MaxDownloads);
    }

    [Fact]
    public async Task Handle_LimitRaisedBetweenSends_InFlightUnaffected() // US-018-04 AC block 2.
    {
        _limits.Record = SendLimits(7, 100);
        var a = NewDraftTransfer();
        _store.Transfer = a;
        await _handler.Handle(new SendTransferCommand(a.Id, null, null, null, []), CancellationToken.None);
        var aExpiry = a.ExpiresAtUtc;

        _limits.Record = SendLimits(14, 250);
        var b = NewDraftTransfer();
        _store.Transfer = b;
        await _handler.Handle(new SendTransferCommand(b.Id, null, null, null, []), CancellationToken.None);

        Assert.Equal(aExpiry, a.ExpiresAtUtc);
        Assert.Equal(100, a.MaxDownloads);
        Assert.InRange(b.ExpiresAtUtc, DateTime.UtcNow.AddDays(14).AddMinutes(-1), DateTime.UtcNow.AddDays(14).AddMinutes(1));
        Assert.Equal(250, b.MaxDownloads);
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

    // ---- T-010-03 (US-010-02): re-send drafts clear their copied hash when no new password is provided ----

    [Fact]
    public async Task Send_ResendDraft_NoPassword_ClearsCopiedHash()
    {
        // Seed a SourcedFrom draft with PasswordHash set (copied from the original by T-010-01).
        var transferId = Guid.NewGuid();
        var originalTransferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow,
            SourcedFrom = originalTransferId,
            PasswordHash = "copied-hash-from-original" // T-010-01 copies this byte-for-byte
        };

        await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, []),
            CancellationToken.None);

        // The copied hash is cleared — the original's PBKDF2 key doesn't match this draft's ID.
        Assert.Null(_store.Transfer!.PasswordHash);
    }

    [Fact]
    public async Task Send_ResendDraft_NewPassword_ReKeysWithNewId()
    {
        // Seed a SourcedFrom draft with PasswordHash set (copied from the original by T-010-01).
        var transferId = Guid.NewGuid();
        var originalTransferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow,
            SourcedFrom = originalTransferId,
            PasswordHash = "copied-hash-from-original"
        };

        await _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, [], "newpass"),
            CancellationToken.None);

        // The hash is re-keyed with the NEW transfer's ID.
        var hash = _store.Transfer!.PasswordHash;
        Assert.NotNull(hash);
        Assert.NotEqual("copied-hash-from-original", hash);

        // Verify the hash can be verified against the NEW transfer's ID.
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
        var verifyResult = hasher.VerifyHashedPassword(transferId.ToString("n"), hash, "newpass");
        Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success, verifyResult);
    }

    [Fact]
    public async Task Normal_Draft_NoPassword_KeepsNull()
    {
        // SourcedFrom=null draft (normal draft) — no behavior change.
        var transferId = Guid.NewGuid();
        _store.Transfer = new Transfer
        {
            Id = transferId,
            LinkId = "ABCD1234",
            Status = 0,
            TotalBytes = 100,
            FileCount = 1,
            SenderName = "Guest",
            CreatedAtUtc = DateTime.UtcNow,
            SourcedFrom = null // normal draft — not a re-send
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

    // ---- US-020-02 (FR-020-2): send-time active-transfers re-check ----

    private static LimitsRecord RecordWithActiveMax(int activeTransfersMax) => new()
    {
        MaxEmails = 20,
        RetentionDays = 30,
        MaxTransferSize = 5_368_709_120,
        MaxSingleFile = 5_368_709_120,
        ActiveTransfersMax = activeTransfersMax
    };

    [Fact]
    public async Task Handle_OwnerAtActiveTransfersMax_Throws_NamesLimitAndUpgrade_NoMutation()
    {
        // AC-020-1: the (max+1)th live transfer is the one refused; checks precede mutation.
        var transferId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _meterStore.ActiveTransferCount = 20;

        var ex = await Assert.ThrowsAsync<ActiveTransfersExceededException>(() =>
            _handler.Handle(new SendTransferCommand(transferId, null, null, null, ["a@x.com"], null, owner), CancellationToken.None));

        Assert.Equal(20, ex.CurrentCount);
        Assert.Equal(20, ex.MaxActiveTransfers);
        Assert.Contains("(limit 20). Upgrade to keep sending.", ex.Message);
        Assert.Equal(0, _store.Transfer!.Status);
        Assert.Empty(_eventPublisher.Published);
        Assert.Empty(_store.Recipients);
    }

    [Fact]
    public async Task Handle_OwnerOverQuotaAfterDowngrade_100Active_Blocked()
    {
        // AC-020-2: downgraded to Free with 100 live transfers — new send blocked, nothing else touched.
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _meterStore.ActiveTransferCount = 100;

        var ex = await Assert.ThrowsAsync<ActiveTransfersExceededException>(() =>
            _handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(100, ex.CurrentCount);
        Assert.Equal(20, ex.MaxActiveTransfers);
    }

    [Fact]
    public async Task Handle_OwnerUnderProLimit_100Active_Sends()
    {
        // EC-020-2 / story AC 2: re-upgraded (Pro max 200) — the same 100 live transfers no longer block.
        var handler = NewHandler(new StubLimitsProvider { Record = RecordWithActiveMax(200) });
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _meterStore.ActiveTransferCount = 100;

        var result = await handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
    }

    [Fact]
    public async Task Handle_InfiniteActiveTransfersMax_SkipsCount()
    {
        var handler = NewHandler(new StubLimitsProvider { Record = RecordWithActiveMax(-1) });
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _meterStore.ActiveTransferCount = 10_000;

        var result = await handler.Handle(new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
    }

    [Fact]
    public async Task Handle_Guest_SkipsActiveTransfersCheck()
    {
        // FR-020-4 / AC-020-5: guests never hit account-level limits.
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        _meterStore.ActiveTransferCount = 100;

        var result = await _handler.Handle(new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Status);
    }

    // ---- US-022-01 (F-PRF-002): schedule at create ----

    private static object? PayloadField(EventEnvelope envelope, string name) =>
        envelope.Payload!.GetType().GetProperty(name)!.GetValue(envelope.Payload);

    private SendTransferHandler ProHandler() =>
        NewHandler(new StubLimitsProvider { Features = new PlanFeatures { Scheduling = true } });

    [Fact]
    public async Task Send_Scheduled_ProPlan_StoresUtcAndStatusScheduled()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        var due = DateTime.UtcNow.AddHours(2);

        var result = await ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid(), due), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(TransferStatus.Scheduled, _store.Transfer!.Status);
        Assert.Equal(due, _store.Transfer.ScheduledSendAtUtc);
        Assert.Equal(5, result!.Status);
        Assert.Equal(due, result.ScheduledSendAtUtc);
    }

    [Fact]
    public async Task Send_Scheduled_EmitsTransferCreatedNow_WithScheduledSendAt()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        var due = DateTime.UtcNow.AddHours(3);

        await ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, ["a@x.com"], null, Guid.NewGuid(), due), CancellationToken.None);

        var envelope = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.TransferCreated, envelope.Type);
        Assert.Equal(due, PayloadField(envelope, "scheduledSendAt"));
    }

    [Fact]
    public async Task Send_Scheduled_ExpiresAtFromCreation_NotFromDue()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        var before = DateTime.UtcNow;

        await ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid(), before.AddDays(10)), CancellationToken.None);

        var after = DateTime.UtcNow;
        var retention = TimeSpan.FromDays(30); // StubLimitsProvider RetentionDays
        Assert.InRange(_store.Transfer!.ExpiresAtUtc, before + retention, after + retention);
    }

    [Fact]
    public async Task Send_Scheduled_PlanWithoutScheduling_ThrowsScheduleForbidden_NoWrite()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        await Assert.ThrowsAsync<ScheduleForbiddenException>(() => _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, ["a@x.com"], null, null, DateTime.UtcNow.AddHours(2)), CancellationToken.None));

        Assert.Equal(0, _store.SaveSendCalls);
        Assert.Empty(_eventPublisher.Published);
        Assert.Equal(0, _store.Transfer!.Status);
    }

    // ---- US-022-03 (FR-022-2): schedule window, server time ----

    private async Task AssertOutOfWindowNoWrite(DateTime due, string messagePart)
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        var ex = await Assert.ThrowsAsync<ScheduleOutOfWindowException>(() => ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, ["a@x.com"], null, Guid.NewGuid(), due), CancellationToken.None));

        Assert.Contains(messagePart, ex.Message);
        Assert.Equal(0, _store.SaveSendCalls);
        Assert.Empty(_eventPublisher.Published);
        Assert.Equal(0, _store.Transfer!.Status);
    }

    [Fact]
    public Task Send_Scheduled_PlusTwoMinutes_ThrowsOutOfWindow_NoWrite() =>
        AssertOutOfWindowNoWrite(DateTime.UtcNow.AddMinutes(2), "5 minutes");

    [Fact]
    public Task Send_Scheduled_PlusThirtyDays_ThrowsOutOfWindow_NoWrite() =>
        AssertOutOfWindowNoWrite(DateTime.UtcNow.AddDays(30), "14 days");

    [Fact]
    public Task Send_Scheduled_InPast_ThrowsOutOfWindow() =>
        AssertOutOfWindowNoWrite(DateTime.UtcNow.AddHours(-1), "5 minutes");

    [Theory]
    [InlineData(true)]    // +5 min 30 s
    [InlineData(false)]   // +14 d -30 s
    public async Task Send_Scheduled_JustInsideWindow_Accepted(bool nearMin)
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);
        var due = nearMin
            ? DateTime.UtcNow + wa.domain.Transfers.ScheduleWindow.Min + TimeSpan.FromSeconds(30)
            : DateTime.UtcNow + wa.domain.Transfers.ScheduleWindow.Max - TimeSpan.FromSeconds(30);

        var result = await ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, [], null, Guid.NewGuid(), due), CancellationToken.None);

        Assert.Equal(5, result!.Status);
    }

    [Fact]
    public async Task Send_Scheduled_FreePlanAndOutOfWindow_ForbiddenWins()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        await Assert.ThrowsAsync<ScheduleForbiddenException>(() => _handler.Handle(
            new SendTransferCommand(transferId, null, null, null, ["a@x.com"], null, null, DateTime.UtcNow.AddDays(30)), CancellationToken.None));

        Assert.Equal(0, _store.SaveSendCalls);
    }

    [Fact]
    public async Task Send_NoSchedule_StaysActive_ScheduledSendAtNull()
    {
        var transferId = Guid.NewGuid();
        _store.Transfer = NewDraftTransfer(transferId);

        var result = await ProHandler().Handle(
            new SendTransferCommand(transferId, null, null, null, []), CancellationToken.None);

        Assert.Equal(1, result!.Status);
        Assert.Null(result.ScheduledSendAtUtc);
        Assert.Null(_store.Transfer!.ScheduledSendAtUtc);
        var envelope = Assert.Single(_eventPublisher.Published);
        Assert.Null(PayloadField(envelope, "scheduledSendAt"));
    }

    // ---- Stubs ----

    private sealed class StubSendStore : ISendStore
    {
        public Transfer? Transfer { get; set; }
        public int SaveSendCalls { get; private set; }
        public List<EmailRecipient> Recipients { get; } = [];
        // T-010-01 (row 3): the three new ISendStore members — no-op fakes for the send-path tests.
        public Dictionary<Guid, string[]> RecipientAddressesById { get; } = [];
        public Dictionary<Guid, FileItemRow[]> FilesById { get; } = [];
        public (Transfer Draft, IReadOnlyList<FileItem> FileItems, IReadOnlyList<EmailRecipient> Recipients, IReadOnlyList<BlobRef> SharedBlobRefs)? ResendCapture { get; private set; }

        public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Transfer is not null && Transfer.Id == id ? Transfer : null);

        public Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
        {
            SaveSendCalls++;
            Recipients.AddRange(recipients);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult(RecipientAddressesById.TryGetValue(transferId, out var a) ? (IReadOnlyList<string>)a : (IReadOnlyList<string>)[]);

        public Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult(FilesById.TryGetValue(transferId, out var f) ? (IReadOnlyList<FileItemRow>)f : (IReadOnlyList<FileItemRow>)[]);

        public Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct)
        {
            ResendCapture = (draft, fileItems, recipients, sharedBlobRefs);
            return Task.CompletedTask;
        }
    }

    // Plays IPlanContextAccessor since US-020-02.
    private sealed class StubLimitsProvider : IPlanContextAccessor
    {
        // T-007-04: Free values from the Part 2 frozen table — MaxTransferSize/MaxSingleFile must be set
        // (the new send-time re-check trips on 0 in every happy-path test). Tests may override per-scenario.
        public LimitsRecord Record { get; set; } = new()
        {
            MaxEmails = 20,
            RetentionDays = 30,
            MaxTransferSize = 5_368_709_120, // 5 GB (Free) — same literal FinalizeTransferHandlerTests pins for the same reason.
            MaxSingleFile = 5_368_709_120,    // same as transfer size for all plans
            ActiveTransfersMax = 20           // US-020-02: Free value (Part 2) — 0 would trip the owned-send active-transfers check
        };

        public PlanFeatures Features { get; set; } = new();   // US-022-01: Scheduling defaults false (Free/guest)

        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext(ILimitsProvider.GuestPlanCode, Record, Features));
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

        /// <summary>US-020-02: what the account active-transfers COUNT returns — set per scenario.</summary>
        public int ActiveTransferCount { get; set; } = 0;

        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(ActiveTransferCount);

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
