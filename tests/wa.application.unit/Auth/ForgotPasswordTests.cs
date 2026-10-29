using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Auth;

// T-008-05 (FR-008-6 / TA-4.2#11): anti-enumeration forgot-password handler — the only business logic behind
// POST /api/v1/auth/forgot-password. Registered email → Purpose=2 AuthToken row + auth.magic_link_requested event
// (purpose=2 discriminator, D-3); unregistered email → NO row, NO event, same result record (byte-identical 202).
public class ForgotPasswordTests
{
    private readonly StubAppUserStore _users;
    private readonly StubAuthTokenStore _tokens;
    private readonly StubEventPublisher _publisher;
    private readonly ForgotPasswordHandler _handler;

    public ForgotPasswordTests()
    {
        _users = new StubAppUserStore();
        _tokens = new StubAuthTokenStore();
        _publisher = new StubEventPublisher();
        _handler = new ForgotPasswordHandler(_users, _tokens, _publisher);
    }

    // ---- FR-008-6 exit check 1: registered email → Purpose=2 row + event with purpose=2 in payload ----

    [Fact]
    public async Task RegisteredEmail_CreatesTokenRowPurpose2_AndPublishesMagicLinkEvent()
    {
        _users.FoundUser = new AppUser(); // any non-null user = "registered"

        var before = DateTime.UtcNow;
        await _handler.Handle(new ForgotPasswordCommand("user@example.com"), CancellationToken.None);
        var after = DateTime.UtcNow;

        Assert.Single(_tokens.Created);
        var stored = _tokens.Created[0];

        Assert.Equal(2, stored.Purpose); // TA-3.2: AuthToken.Purpose=2 for forgot-password (registry row)
        Assert.Equal(64, stored.TokenHash.Length); // SHA-256 hex — exactly 64 chars (TA-9.1)
        Assert.Matches("^[0-9a-f]{64}$", stored.TokenHash);

        // Frozen 10-min TTL — referenced from RequestMagicLinkHandler (no re-declared number).
        Assert.Equal(RequestMagicLinkHandler.TtlSeconds, (int)(stored.ExpiresAtUtc - stored.CreatedAtUtc).TotalSeconds);
        Assert.InRange(stored.ExpiresAtUtc, before.AddMinutes(10), after.AddMinutes(10));

        // The event is auth.magic_link_requested (D-2: 12-member closed list — no new type) with purpose=2 payload.
        Assert.Single(_publisher.Envelopes);
        var envelope = _publisher.Envelopes[0];
        Assert.Equal(EventType.MagicLinkRequested, envelope.Type);
        Assert.Equal("user@example.com", envelope.PartitionKey);

        var payload = JsonSerializerHelper.PayloadJson(envelope.Payload!);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal("user@example.com", doc.RootElement.GetProperty("email").GetString());

        // The raw token is base64 (32 bytes → 44 chars) and the stored hash is its SHA-256 hex (recomputable).
        var rawToken = doc.RootElement.GetProperty("token").GetString()!;
        Assert.Equal(44, rawToken.Length); // base64 of 32 bytes — f-email puts it in ?ml={token}
        Assert.NotEqual(stored.TokenHash, rawToken);

        using var sha = System.Security.Cryptography.SHA256.Create();
        var expectedHash = System.Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        Assert.Equal(expectedHash, stored.TokenHash);

        // D-3 discriminator: purpose=2 in the payload — EmailFunction routes on it.
        Assert.Equal(2, doc.RootElement.GetProperty("purpose").GetInt32());

        Assert.NotNull(doc.RootElement.GetProperty("expiresAtUtc")); // expiresAtUtc present (same shape as magic-link)
    }

    // ---- FR-008-6 exit check 2: unregistered email → NO row, NO event — same result record (anti-enumeration) ----

    [Fact]
    public async Task UnregisteredEmail_NoTokenRowNoEvent_ReturnsSameResult()
    {
        _users.FoundUser = null; // not registered

        var result = await _handler.Handle(new ForgotPasswordCommand("unknown@example.com"), CancellationToken.None);

        Assert.Empty(_tokens.Created); // no row created (exit check 2)
        Assert.Empty(_publisher.Envelopes); // no event — nothing for f-email to send
        Assert.NotNull(result); // same 202 marker record → byte-identical response (FR-008-6: no "email not found")
    }

    [Fact]
    public async Task RegisteredEmail_UsesUserStoreBeforeAnyTokenWork()
    {
        // The existence check runs on the normalized email, before any token work.
        _users.FoundUser = new AppUser();

        await _handler.Handle(new ForgotPasswordCommand("  USER@Example.com "), CancellationToken.None);

        Assert.Single(_users.Searches);
        Assert.Equal("user@example.com", _users.Searches[0]); // lowercased + trimmed lookup (TA-4.2#8)
        Assert.Equal("user@example.com", _tokens.Created[0].Email!); // row Email is lowercase (exit check: normalization)
    }

    // ---- Invalid email → validation exception before any store/event call (D-1 step 1, D-6) ----

    [Fact]
    public async Task InvalidEmail_ThrowsValidation_NoRowNoEvent()
    {
        await Assert.ThrowsAsync<ForgotPasswordValidationException>(async () =>
            await _handler.Handle(new ForgotPasswordCommand("not-an-email"), CancellationToken.None));

        Assert.Empty(_users.Searches); // existence check never runs on invalid input
        Assert.Empty(_tokens.Created); // no row created on validation failure
        Assert.Empty(_publisher.Envelopes); // no event emitted on validation failure
    }

    [Fact]
    public async Task EmptyEmail_ThrowsValidation_NoRowNoEvent()
    {
        await Assert.ThrowsAsync<ForgotPasswordValidationException>(async () =>
            await _handler.Handle(new ForgotPasswordCommand("   "), CancellationToken.None));

        Assert.Empty(_users.Searches); // existence check never runs on invalid input
        Assert.Empty(_tokens.Created); // no row created on validation failure
        Assert.Empty(_publisher.Envelopes); // no event emitted on validation failure
    }

    // ---- Stubs (T-014 style — hand-written stubs, no Moq) ----

    /// <summary>IAppUserStore stub — the existence check (D-1). FoundUser is null by default = unregistered; Searches records the lookup emails.</summary>
    private sealed class StubAppUserStore : IAppUserStore
    {
        public AppUser? FoundUser { get; set; }
        public List<string> Searches { get; } = [];

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct)
        {
            Searches.Add(lowercasedEmail);
            return Task.FromResult(FoundUser);
        }

        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask; // signup-only — unused here

        // T-008-06 (D-1): compile fix only — the ForgotPassword handler doesn't call these new members.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(FoundUser);
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubAuthTokenStore : IAuthTokenStore
    {
        public List<AuthToken> Created { get; } = [];

        public Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct) =>
            Task.FromResult(Created.FirstOrDefault(t => t.TokenHash == sha256Hex));

        public async Task CreateAsync(AuthToken token, CancellationToken ct)
        {
            Created.Add(token);
            await Task.CompletedTask;
        }

        public async Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct)
        {
            var t = Created.FirstOrDefault(x => x.Id == id);
            if (t is null || t.RedeemedAtUtc is not null) return;
            t.RedeemedAtUtc = redeemedAtUtc;
            await Task.CompletedTask;
        }
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Envelopes { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    /// <summary>Serializes the anonymous payload to JSON without depending on the real publisher's options.</summary>
    private static class JsonSerializerHelper
    {
        public static string PayloadJson(object? payload) =>
            System.Text.Json.JsonSerializer.Serialize(payload);
    }
}
