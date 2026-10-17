using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-002-04: the handler is the only business logic behind POST /api/v1/transfers/{id}/send (TA-4.2a).
// Activate Draft→Active, store normalized EmailRecipient rows, emit transfer.created via outbox.
public class SendTransferHandlerTests
{
    private readonly StubSendStore _store;
    private readonly StubLimitsProvider _limits;
    private readonly StubEventPublisher _eventPublisher;
    private readonly SendTransferHandler _handler;

    public SendTransferHandlerTests()
    {
        _store = new StubSendStore();
        _limits = new StubLimitsProvider();
        _eventPublisher = new StubEventPublisher();
        _handler = new SendTransferHandler(_store, _limits, _eventPublisher, Microsoft.Extensions.Logging.Abstractions.NullLogger<SendTransferHandler>.Instance);
    }

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
        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LimitsRecord { MaxEmails = 20, RetentionDays = 30 });
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
}
