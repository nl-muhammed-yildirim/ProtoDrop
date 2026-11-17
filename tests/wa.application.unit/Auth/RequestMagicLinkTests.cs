using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Auth;

// T-008-04 (TA-9.1 / FR-008-1): the handler is the only business logic behind POST /api/v1/auth/magic-link (TA-4.2a).
// Token stored as SHA-256 hex (only the hash is persisted — TA-9.1); RAW token goes in the event payload.
public class RequestMagicLinkTests
{
    private readonly StubAuthTokenStore _tokens;
    private readonly StubEventPublisher _publisher;
    private readonly RequestMagicLinkHandler _handler;

    public RequestMagicLinkTests()
    {
        _tokens = new StubAuthTokenStore();
        _publisher = new StubEventPublisher();
        _handler = new RequestMagicLinkHandler(_tokens, _publisher);
    }

    // ---- FR-008-1: token stored as SHA-256 hex with Purpose=1 and 10-min TTL ----

    [Fact]
    public async Task Request_StoresTokenAsSha256Hex_Purpose1_AndTenMinuteTtl()
    {
        var before = DateTime.UtcNow;
        await _handler.Handle(new RequestMagicLinkCommand("User@Example.COM"), CancellationToken.None);
        var after = DateTime.UtcNow;

        Assert.Single(_tokens.Created);
        var stored = _tokens.Created[0];

        Assert.Equal("user@example.com", stored.Email); // normalized — same rule as signup (TA-4.2#8)
        Assert.Equal(1, stored.Purpose); // TA-3.2: AuthToken.Purpose=1 for magic-link-login
        Assert.Equal(64, stored.TokenHash.Length); // SHA-256 hex — exactly 64 chars (TA-9.1)
        Assert.Matches("^[0-9a-f]{64}$", stored.TokenHash);
        Assert.Null(stored.RedeemedAtUtc); // unused until redeem (single-use)

        var ttl = stored.ExpiresAtUtc - stored.CreatedAtUtc;
        Assert.Equal(RequestMagicLinkHandler.TtlSeconds, (int)ttl.TotalSeconds); // frozen 10 min — no magic number
        Assert.InRange(stored.ExpiresAtUtc, before.AddMinutes(10), after.AddMinutes(10));

        // TA-9.1: the raw token is NOT persisted — only its hash is recoverable from the row.
        Assert.False(string.IsNullOrEmpty(stored.TokenHash));
        Assert.NotEqual(stored.TokenHash, stored.Email);
    }

    [Fact]
    public async Task Request_PublishesEvent_WithRawTokenPayload_NotTheHash()
    {
        // TA-9.1: only the hash is persisted — the RAW token goes in the event payload (f-email builds the link).
        var result = await _handler.Handle(new RequestMagicLinkCommand("user@example.com"), CancellationToken.None);

        Assert.Single(_publisher.Envelopes);
        var envelope = _publisher.Envelopes[0];

        Assert.Equal(EventType.MagicLinkRequested, envelope.Type); // D-6: 12th member of the closed list
        Assert.Equal("user@example.com", envelope.PartitionKey);

        // The payload carries email + raw token (base64) + expiresAtUtc — shape pinned by plan §3.
        var payload = JsonSerializerHelper.PayloadJson(envelope.Payload!);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal("user@example.com", doc.RootElement.GetProperty("email").GetString());

        // The raw token is base64 (32 bytes → 44 chars) and DIFFERS from the stored hash (TA-9.1).
        var rawToken = doc.RootElement.GetProperty("token").GetString()!;
        Assert.Equal(44, rawToken.Length); // base64 of 32 bytes — the value f-email puts in ?ml={token}
        Assert.NotEqual(_tokens.Created[0].TokenHash, rawToken);

        // The stored hash must be the SHA-256 hex of the RAW token (recomputable, not leaked).
        using var sha = System.Security.Cryptography.SHA256.Create();
        var expectedHash = System.Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        Assert.Equal(expectedHash, _tokens.Created[0].TokenHash);

        Assert.NotNull(result); // 202 result — no body fields (TA-4.2#10)
    }

    // ---- Invalid email → validation exception (no row, no event) ----

    [Fact]
    public async Task Request_InvalidEmail_ThrowsValidation_NoRowNoEvent()
    {
        await Assert.ThrowsAsync<RequestMagicLinkValidationException>(async () =>
            await _handler.Handle(new RequestMagicLinkCommand("not-an-email"), CancellationToken.None));

        Assert.Empty(_tokens.Created); // no row created on validation failure
        Assert.Empty(_publisher.Envelopes); // no event emitted on validation failure
    }

    [Fact]
    public async Task Request_EmptyEmail_ThrowsValidation_NoRowNoEvent()
    {
        await Assert.ThrowsAsync<RequestMagicLinkValidationException>(async () =>
            await _handler.Handle(new RequestMagicLinkCommand("   "), CancellationToken.None));

        Assert.Empty(_tokens.Created);
        Assert.Empty(_publisher.Envelopes);
    }

    [Fact]
    public async Task Request_NormalizesEmail_LowercaseTrimmedBeforeSaveAndEvent()
    {
        await _handler.Handle(new RequestMagicLinkCommand("  Test@EXAMPLE.com  "), CancellationToken.None);

        Assert.Equal("test@example.com", _tokens.Created[0].Email!); // trimmed + lowercased
        var payload = JsonSerializerHelper.PayloadJson(_publisher.Envelopes[0].Payload!);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal("test@example.com", doc.RootElement.GetProperty("email").GetString());
    }

    // ---- Stubs (T-014 style — hand-written stubs, no Moq) ----

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
