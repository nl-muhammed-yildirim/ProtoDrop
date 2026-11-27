using System.Text.Json;
using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-07 (TA-4.2#22 / FR-011-2): DELETE /api/v1/admin/users/{id} — SuperAdmin-only force-delete.
// The handler's contract: null = unknown user OR already soft-deleted (endpoint → 404 NOT_FOUND);
// non-null = the row was soft-deleted + re-homed and account_deleted emitted once via outbox (TA-5.3).
public class AdminForceDeleteUserCommandTests
{
    private readonly StubAppUserStore _users;
    private readonly StubEventPublisher _eventPublisher;
    private readonly RecordingBlobs _blobs = new();
    private readonly AdminForceDeleteUserHandler _handler;

    public AdminForceDeleteUserCommandTests()
    {
        _users = new StubAppUserStore();
        _eventPublisher = new StubEventPublisher();
        _handler = new AdminForceDeleteUserHandler(_users, _eventPublisher, _blobs, TimeProvider.System);
    }

    // ---- Exit check: unknown user → null (endpoint → 404 NOT_FOUND) ----

    [Fact]
    public async Task UserNotFound_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        _users.User = null; // no row for the user id

        var result = await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        Assert.Null(result); // endpoint maps to 404 NOT_FOUND
        Assert.Empty(_eventPublisher.Published); // no event — the delete did not happen
    }

    // ---- Exit check: happy path — re-home + account_deleted emitted exactly once, PartitionKey == user.Email, Payload carries userId ----

    [Fact]
    public async Task Success_ReHomesAndEmitsAccountDeletedOnce()
    {
        var userId = Guid.NewGuid();
        _users.User = NewUser(userId); // user exists (not soft-deleted)

        await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        // The store's SoftDeleteWithReHomeAsync was called (the re-home happens inside the store — T-008-06).
        Assert.Equal(1, _users.SoftDeleteCallCount);
        Assert.Equal(userId, _users.CapturedUserId);

        // account_deleted emitted exactly once.
        var envelopes = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.AccountDeleted, envelopes.Type);
        // PartitionKey = user.Email (same partitioning as the other auth.* events — DeleteAccountHandler L194).
        Assert.Equal("ada@x.com", envelopes.PartitionKey);
        // Payload = { userId } (TA-5.3 closed set — PII sweep reference).
        var payloadJson = JsonSerializerHelper.PayloadJson(envelopes.Payload!);
        using var doc = JsonDocument.Parse(payloadJson);
        Assert.Equal(userId, doc.RootElement.GetProperty("userId").GetGuid());
    }

    // ---- Exit check: result carries the email PRE-delete (so the endpoint can audit {email} post-soft-delete) ----

    [Fact]
    public async Task Success_ReturnsEmailInResult()
    {
        var userId = Guid.NewGuid();
        _users.User = NewUser(userId); // user exists (not soft-deleted)

        var result = await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(userId, result.UserId);
        Assert.Equal("ada@x.com", result.Email); // email captured pre-delete — the row is unreadable after SoftDeleteWithReHomeAsync
    }

    // ---- Exit check: race — SoftDeleteWithReHomeAsync returns false → null (404) ----

    [Fact]
    public async Task SoftDeleteReturnsFalse_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        _users.User = NewUser(userId); // user read OK
        _users.SoftDeleteResult = false; // race: a concurrent DELETE already committed

        var result = await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        Assert.Null(result); // endpoint maps to 404 NOT_FOUND — same not-found shape
        Assert.Empty(_eventPublisher.Published); // no event — the delete did not happen (the store returned false)
    }

    // ---- US-021-04 (decision 4): the org logo is deleted with the account ----

    [Fact]
    public async Task ForceDelete_WithLogo_DeletesLogoBlob()
    {
        var userId = Guid.NewGuid();
        var user = NewUser(userId);
        user.OrgLogoBlobPath = $"avatars/{userId:N}/logo";
        _users.User = user;

        await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        Assert.Equal([$"avatars/{userId:N}/logo"], _blobs.Deleted);
    }

    [Fact]
    public async Task ForceDelete_LogoDeleteFails_StillReturnsResult()
    {
        var userId = Guid.NewGuid();
        var user = NewUser(userId);
        user.OrgLogoBlobPath = $"avatars/{userId:N}/logo";
        _users.User = user;
        _blobs.Throw = true;

        var result = await _handler.Handle(new AdminForceDeleteUserCommand(userId), CancellationToken.None);

        Assert.NotNull(result); // the account is already deleted; a storage failure must not become a 500
        Assert.Equal(1, _users.SoftDeleteCallCount);
    }

    // ---- Helpers ----

    private static class JsonSerializerHelper
    {
        public static string PayloadJson(object? payload) =>
            JsonSerializer.Serialize(payload);
    }

    private static AppUser NewUser(Guid id) => new()
    {
        Id = id,
        Email = "ada@x.com",
        PlanId = Guid.NewGuid(),
        DeletedAtUtc = null // not soft-deleted — GetByIdAsync returns this row
    };

    // ---- Stubs (hand-written, no Moq — mirrors DeleteAccountHandler / AdminDeleteTransferCommand event-once tests) ----

    private sealed class RecordingBlobs : IBlobStore
    {
        public List<string> Deleted { get; } = [];
        public bool Throw { get; set; }
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("storage down");
            Deleted.Add(path);
            return Task.CompletedTask;
        }
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public AppUser? User { get; set; }
        public int SoftDeleteCallCount { get; private set; }
        public Guid CapturedUserId { get; private set; }
        public bool SoftDeleteResult { get; set; } = true;

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
            Task.FromResult(User);

        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;

        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(User is not null && User.Id == userId ? User : null);

        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(true);
        public Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
        {
            SoftDeleteCallCount++;
            CapturedUserId = userId;
            return Task.FromResult(SoftDeleteResult);
        }
    }
}
