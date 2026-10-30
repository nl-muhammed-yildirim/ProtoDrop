using System.Security.Cryptography;
using wa.application.UseCases.Auth;

namespace wa.application.unit.Auth;

// T-008-02 (TA-9.1 PARSE + FR-008-2 rolling threshold): SessionTokenService is the single minter+reader class,
// so the cookie format can't drift. Parse returns null on missing/malformed/tampered/expired (guest path).
public class SessionTokenServiceTests
{
    private static readonly byte[] Key = System.Text.Encoding.UTF8.GetBytes("unit-test-session-key");

    // ---- TA-9.1 PARSE side: valid token → user id + expiry ----

    [Fact]
    public void Parse_Valid_ReturnsUserIdAndExp()
    {
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var userId = Guid.NewGuid();
        var token = svc.Mint(userId);

        var parsed = svc.Parse(token);

        Assert.NotNull(parsed);
        Assert.Equal(userId, parsed.Value.UserId);
        // exp ≈ now + LifetimeDays (30 d) — the frozen TTL.
        var diff = parsed.Value.ExpUtc - DateTime.UtcNow;
        Assert.InRange(diff, TimeSpan.FromMinutes(-1), TimeSpan.FromDays(SessionTokenService.LifetimeDays));
    }

    [Fact]
    public void Parse_TamperedSignature_ReturnsNull()
    {
        // Tamper the signature segment → HMAC check fails → guest (AC-008-1 cookie round-trip).
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var token = MintToken(svc, Guid.NewGuid());
        var tampered = TokenWithSig(token, "bbbbbb");

        Assert.Null(svc.Parse(tampered));
    }

    [Fact]
    public void Parse_Expired_ReturnsNull()
    {
        // A correctly signed token in the past → expired → guest (AC-008-1 cookie round-trip).
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var token = SignWithExp(svc, Guid.NewGuid(), -1);

        Assert.Null(svc.Parse(token));
    }

    [Fact]
    public void Parse_Malformed_ReturnsNull()
    {
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);

        Assert.Null(svc.Parse(null));
        Assert.Null(svc.Parse(""));
        Assert.Null(svc.Parse("not-base64!!!"));
        // Base64 of a 1-segment string (no '|' splits) → malformed.
        Assert.Null(svc.Parse(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("onlyone"))));
    }

    [Fact]
    public void Parse_WrongKey_ReturnsNull()
    {
        // Token minted with a different key → signature mismatch → guest (key rotation = everyone logged out).
        var otherKey = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var token = otherKey.Mint(Guid.NewGuid());
        var thisSvc = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("a-different-key!!"), () => DateTimeOffset.UtcNow);

        Assert.Null(thisSvc.Parse(token));
    }

    // ---- FR-008-2 / TA-9.1 rolling threshold: 15 days remaining (the frozen renew boundary) ----

    [Fact]
    public void NeedsRenewal_AtBoundary_Refreshes()
    {
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var now = DateTime.UtcNow;

        // Exactly 15 d remaining → at the threshold → true (refresh). Use AddDays (AddSeconds(double) loses ~4µs).
        Assert.True(svc.NeedsRenewal(now.AddDays(15), now));

        // 15 d + 1 s remaining → beyond the threshold → false (no refresh).
        Assert.False(svc.NeedsRenewal(now.AddDays(15).AddSeconds(1), now));
    }

    [Fact]
    public void NeedsRenewal_FreshToken_DoesNotRefresh()
    {
        var svc = new SessionTokenService(Key, () => DateTimeOffset.UtcNow);
        var now = DateTime.UtcNow;

        // 29 d remaining (well above 15) → no renewal.
        Assert.False(svc.NeedsRenewal(now.AddDays(29), now));
    }

    // ---- Helpers: build tokens with controlled fields ----

    /// <summary>Mint a fresh correctly-signed token for the given user id (for tamper tests).</summary>
    private static string MintToken(SessionTokenService svc, Guid userId) =>
        SignWithExp(svc, userId, SessionTokenService.LifetimeDays);

    /// <summary>Build a token with a controlled expiry offset in days (for the expired test).</summary>
    private static string SignWithExp(SessionTokenService svc, Guid userId, double expOffsetDays) =>
        Sign(svc, (DateTimeOffset.UtcNow + TimeSpan.FromDays(expOffsetDays)).ToUnixTimeSeconds(), userId);

    /// <summary>Replace only the signature segment of an already-formed token (for the tamper test).</summary>
    private static string TokenWithSig(string base64Token, string sigSegment)
    {
        var str = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Token));
        var parts = str.Split('|');
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{parts[0]}|{parts[1]}|{sigSegment}"));
    }

    private static string Sign(SessionTokenService svc, long expSeconds, Guid userId)
    {
        var unsigned = $"{userId:N}|{expSeconds}";
        using var hmac = new HMACSHA256(Key);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsigned)));
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }
}
