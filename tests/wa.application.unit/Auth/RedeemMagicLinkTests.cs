using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Auth;

// T-008-04 (TA-9.1 / FR-008-1): redeem validates single-use + expiry, signs in, mints a session token.
// Unknown email → account created on redeem (FR-008-3). Expired ≡ redeemed ≡ unknown → same LinkExpiredException (EC-008-3).
public class RedeemMagicLinkTests
{
    private readonly StubAuthTokenStore _tokens;
    private readonly StubAppUserStore _users;
    private readonly LoginHandlerTests.TelemetryRecorder _telemetryLogger;
    private readonly StubPlansStore _plans;
    private readonly SessionTokenService _minter;
    private readonly RedeemMagicLinkHandler _handler;

    public RedeemMagicLinkTests()
    {
        _tokens = new StubAuthTokenStore();
        _users = new StubAppUserStore();
        _telemetryLogger = new LoginHandlerTests.TelemetryRecorder();
        _plans = new StubPlansStore();
        _minter = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("unit-test-session-key"), () => DateTimeOffset.UtcNow);
        _handler = new RedeemMagicLinkHandler(
            _tokens,
            _users,
            _plans,
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            _minter);
    }

    private string GenerateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string Sha256Hex(string token)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private AuthToken SeedLiveToken(string email)
    {
        var raw = GenerateRawToken();
        _tokens.Raw = raw; // let the test recompute the expected hash from the SAME raw value
        var row = new AuthToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            TokenHash = Sha256Hex(raw),
            Purpose = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(RedeemMagicLinkHandler.TtlSeconds),
            CreatedAtUtc = DateTime.UtcNow
        };
        _tokens.Row = row; // the stub's FindByHashAsync returns this when the hash matches
        return row;
    }

    private AppUser SeedRealAppUser(Guid id, Guid planId) =>
        new()
        {
            Id = id,
            Email = "real@example.com",
            DisplayName = "real",
            Theme = "system",
            PlanId = planId,
            EmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

    // ---- Happy path: valid token → sign in, single-use marked, login_success emitted ----

    [Fact]
    public async Task Redeem_ValidToken_SignsIn_MarksRedeemed_EmitsLoginSuccessOnce()
    {
        var planId = _plans.Plan.Id;
        _users.ExistingUser = SeedRealAppUser(Guid.NewGuid(), planId);
        var tokenRow = SeedLiveToken("real@example.com");

        var result = await _handler.Handle(new RedeemMagicLinkCommand(_tokens.Raw), CancellationToken.None);

        Assert.NotNull(result); // D-4: result carries profile + SessionToken (minted by ISessionTokenMinter)
        Assert.False(string.IsNullOrWhiteSpace(result.SessionToken));
        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginSuccess);
        // Single-use: the row is marked redeemed with a timestamp (AC-008-2).
        Assert.NotNull(tokenRow.RedeemedAtUtc);
    }

    // ---- AC-008-2: magic link clicked twice → second click invalid (token single-use) ----

    [Fact]
    public async Task Redeem_ClickedTwice_SecondClickThrowsLinkExpired()
    {
        var planId = _plans.Plan.Id;
        _users.ExistingUser = SeedRealAppUser(Guid.NewGuid(), planId);
        SeedLiveToken("real@example.com");
        var raw = _tokens.Raw!;

        // First click succeeds.
        var first = await _handler.Handle(new RedeemMagicLinkCommand(raw), CancellationToken.None);
        Assert.NotNull(first);

        // Second click with the SAME token → LinkExpiredException (don't leak it was already used).
        await Assert.ThrowsAsync<LinkExpiredException>(async () =>
            await _handler.Handle(new RedeemMagicLinkCommand(raw), CancellationToken.None));
    }

    // ---- EC-008-3: expired (10 min) ≡ redeemed — identical exception, don't leak which ----

    [Fact]
    public async Task Redeem_ExpiredToken_ThrowsLinkExpired_SameShapeAsRedeemed()
    {
        _users.ExistingUser = SeedRealAppUser(Guid.NewGuid(), _plans.Plan.Id);
        var raw = GenerateRawToken();
        // Row exists but is already expired (10 min elapsed).
        _tokens.Row = new AuthToken
        {
            Id = Guid.NewGuid(),
            Email = "real@example.com",
            TokenHash = Sha256Hex(raw),
            Purpose = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1) // 1 second past expiry
        };

        await Assert.ThrowsAsync<LinkExpiredException>(async () =>
            await _handler.Handle(new RedeemMagicLinkCommand(raw), CancellationToken.None));
    }

    // ---- FR-008-3: sign-up is sign-in — unknown email → account created on redeem ----

    [Fact]
    public async Task Redeem_UnknownEmail_CreatesAppUser_BeforeLoginSuccess_TelemetryOrder()
    {
        var raw = GenerateRawToken();
        _tokens.Row = new AuthToken
        {
            Id = Guid.NewGuid(),
            Email = "new@example.com", // no AppUser row exists for this email yet
            TokenHash = Sha256Hex(raw),
            Purpose = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(RedeemMagicLinkHandler.TtlSeconds)
        };

        var before = DateTime.UtcNow;
        var result = await _handler.Handle(new RedeemMagicLinkCommand(raw), CancellationToken.None);
        var after = DateTime.UtcNow;

        // The AppUser was created on redeem (sign-up is sign-in).
        Assert.NotNull(_users.Created);
        var user = _users.Created!;
        Assert.Equal("new@example.com", user.Email);
        Assert.True(user.EmailConfirmed); // magic link IS the confirmation
        Assert.Null(user.PasswordHash); // no password — magic-link-only account
        Assert.Equal("new", user.DisplayName); // local part of email (FR-008-4)
        Assert.Equal(_plans.Plan.Id, user.PlanId); // free plan seeded via IPlansStore
        Assert.Equal("system", user.Theme);
        Assert.InRange(user.CreatedAtUtc, before, after);

        // D-2: user_created emitted BEFORE login_success.
        var events = _telemetryLogger.Entries.Select(e => e.RenderedText).ToList();
        Assert.Contains(TelemetryEvents.UserCreated, events);
        Assert.Contains(TelemetryEvents.LoginSuccess, events);
        Assert.True(events.IndexOf(TelemetryEvents.UserCreated) < events.IndexOf(TelemetryEvents.LoginSuccess));

        Assert.False(string.IsNullOrWhiteSpace(result.SessionToken));
    }

    // ---- Malformed/missing token → validation exception (endpoint maps to VALIDATION 400) ----

    [Fact]
    public async Task Redeem_MissingToken_ThrowsValidation()
    {
        await Assert.ThrowsAsync<RedeemValidationException>(async () =>
            await _handler.Handle(new RedeemMagicLinkCommand("   "), CancellationToken.None));
    }

    [Fact]
    public async Task Redeem_MalformedBase64_ThrowsValidation_NotLinkExpired()
    {
        // Malformed input is a client error (400) — distinct from unknown hash (410). No leakage.
        await Assert.ThrowsAsync<RedeemValidationException>(async () =>
            await _handler.Handle(new RedeemMagicLinkCommand("not-valid-base64!!!"), CancellationToken.None));
    }

    // ---- Stubs (T-014 style — hand-written stubs, no Moq) ----

    private sealed class StubAuthTokenStore : IAuthTokenStore
    {
        public AuthToken? Row { get; set; } // the row returned by FindByHashAsync
        public string? Raw { get; set; } // last raw token used (for test assertions)

        public Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct) =>
            Task.FromResult(Row is null || Row.TokenHash != sha256Hex ? null : Row);

        public Task CreateAsync(AuthToken token, CancellationToken ct)
        {
            Row = token;
            return Task.CompletedTask;
        }

        public Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct)
        {
            if (Row is not null && Row.Id == id)
                Row.RedeemedAtUtc = redeemedAtUtc;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAppUserStore : IAppUserStore
    {
        public AppUser? ExistingUser { get; set; } // row for FindByEmailAsync (null = unknown email)
        public AppUser? Created { get; set; } // row passed to CreateAsync on redeem-create

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
            Task.FromResult(ExistingUser is not null && ExistingUser.Email == lowercasedEmail ? ExistingUser : null);

        public Task CreateAsync(AppUser user, CancellationToken ct)
        {
            Created = user;
            return Task.CompletedTask;
        }

        // T-008-06 (D-1): compile fix only — the RedeemMagicLink handler doesn't call these new members.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(ExistingUser?.Id == userId ? ExistingUser : null);
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Plan Plan { get; } = new() { Id = Guid.NewGuid(), Code = "free", Name = "Free" };

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            Task.FromResult(Plan);

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plan.Id == planId ? Plan : null);
    }
}
