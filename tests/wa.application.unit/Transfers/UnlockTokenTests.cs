using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using wa.application.UseCases.Transfers;

namespace wa.application.unit.Transfers;

// T-003-04 (D3): BCL-only HS256 mint + verify — unit tests pin exp exactly via an injectable clock rather than approximating.
public class UnlockTokenTests
{
    private const string Secret = "unit-test-key";
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(Secret);

    /// <summary>Pins the clock so exp is deterministic (D3: injectable clock for exact-expiry tests).</summary>
    private sealed class StubClock : IDisposable
    {
        public DateTimeOffset Now { get; set; }
        public Func<DateTimeOffset> Fn => () => Now;
        public void Advance(TimeSpan by) => Now += by;
        public void Dispose() { }
    }

    // ---- Mint round-trip: a token verifies for its sub and is rejected for another ----

    [Fact]
    public void Mint_ThenVerify_ReturnsTrueForMatchingSub()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        Assert.True(minter.Verify(token, "abcdefgh")); // round-trip success — sub matches
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseForWrongSub()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        Assert.False(minter.Verify(token, "ijklmno0")); // sub mismatch — the token is bearer-scoped to one linkId (TA-4.2#5)
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseForNullOrEmptyToken()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        Assert.False(minter.Verify(null, "abcdefgh"));
        Assert.False(minter.Verify("", "abcdefgh")); // absent/empty behaves like junk (TA-9.6 enumeration safety)
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseForJunkToken()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        Assert.False(minter.Verify("garbage", "abcdefgh"));
        Assert.False(minter.Verify("a.b.c.d.e.f", "abcdefgh")); // wrong segment count
        Assert.False(minter.Verify("not-base64!!", "abcdefgh")); // malformed — no exception thrown (TA-9.6)
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseForTamperedSignature()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        // Tamper with the signature (last segment) — one bit flip must break verification.
        var lastDot = token.LastIndexOf('.');
        var sig = token[(lastDot + 1)..];
        var flipped = FlipLastBit(sig[0]);
        var tampered = token[..(lastDot + 1)] + flipped;

        Assert.False(minter.Verify(tampered, "abcdefgh")); // constant-time comparison catches the mismatch
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseForTamperedPayload()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        // Tamper with the payload (middle segment) — the signature covers it, so this must fail.
        var segs = token.Split('.');
        var payload = segs[1];
        var tamperedPayload = FlipLastBit(payload[0]) + payload[1..];
        var tampered = segs[0] + "." + tamperedPayload + "." + segs[2];

        Assert.False(minter.Verify(tampered, "abcdefgh")); // payload mismatch → signature invalid
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsFalseWhenExpired()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");
        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1)); // past exp — the 7-day life is hard (TA-9.1)

        Assert.False(minter.Verify(token, "abcdefgh")); // expired → false even though signature and sub are correct
    }

    [Fact]
    public void Mint_ThenVerify_ReturnsTrueJustBeforeExpiry()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");
        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1)); // still within 7d

        Assert.True(minter.Verify(token, "abcdefgh")); // exp boundary — one second before expiry is still valid
    }

    [Fact]
    public void Mint_ExactExpClaim_IsNowPlusSevenDaysViaPinnedClock()
    {
        using var clock = new StubClock();
        clock.Now = DateTimeOffset.UtcNow; // pin the clock so exp is exact (D3)
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        // Decode the payload (unverified — just reading claims) to assert exp exactly.
        var segs = token.Split('.');
        using var doc = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(Base64UrlDecode(segs[1])));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("exp", out var exp));
        Assert.Equal((clock.Now + TimeSpan.FromDays(7)).ToUnixTimeSeconds(), exp.GetInt64()); // exact 7-day TTL (TA-9.1)
    }

    [Fact]
    public void Mint_ExactClaimSet_IsSubPwdExpJtiOnly()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        var segs = token.Split('.');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(segs[1])));
        var root = doc.RootElement;

        // TA-9.1 exact claim set: exactly 4 claims — sub, pwd, exp, jti (no extras, no missing).
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.True(root.TryGetProperty("sub", out var sub));
        Assert.Equal("abcdefgh", sub.GetString());
        Assert.True(root.TryGetProperty("pwd", out var pwd));
        Assert.True(pwd.GetBoolean()); // pwd is always true on a minted token
        Assert.True(root.TryGetProperty("exp", out _));
        Assert.True(root.TryGetProperty("jti", out var jti));
        Assert.False(string.IsNullOrEmpty(jti.GetString()));
    }

    [Fact]
    public void Mint_HeaderIsHs256Jwt()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        var token = minter.Mint("abcdefgh");

        var segs = token.Split('.');
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(segs[0])));
        var root = doc.RootElement;

        Assert.Equal("HS256", root.GetProperty("alg").GetString()); // the header declares HS256 (TA-4.2#5)
        Assert.Equal("JWT", root.GetProperty("typ").GetString());
    }

    [Fact]
    public void Verify_ReturnsFalseWhenPwdClaimIsFalse()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        // Craft a token with pwd=false — the minted shape requires true, so this must be rejected.
        var now = clock.Now;
        var payloadJson = JsonSerializer.Serialize(new
        {
            sub = "abcdefgh",
            pwd = false, // deliberately false — should fail verification
            exp = (now + TimeSpan.FromDays(7)).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("n")
        });
        var headerSeg = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"HS256\"}"));
        var payloadSeg = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = headerSeg + "." + payloadSeg;
        using var hmac = new System.Security.Cryptography.HMACSHA256(Key);
        var sig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        var token = $"{headerSeg}.{payloadSeg}.{sig}";

        Assert.False(minter.Verify(token, "abcdefgh")); // pwd != true → rejected (TA-9.1: the unlock marker must be true)
    }

    [Fact]
    public void Verify_ReturnsFalseWhenPwdClaimMissing()
    {
        using var clock = new StubClock();
        var minter = new UnlockTokenService(Key, clock.Fn);

        // Craft a token without pwd — the minted shape requires it, so this must be rejected.
        var now = clock.Now;
        var payloadJson = JsonSerializer.Serialize(new
        {
            sub = "abcdefgh",
            // no pwd claim
            exp = (now + TimeSpan.FromDays(7)).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("n")
        });
        var headerSeg = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"HS256\"}"));
        var payloadSeg = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = headerSeg + "." + payloadSeg;
        using var hmac = new System.Security.Cryptography.HMACSHA256(Key);
        var sig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        var token = $"{headerSeg}.{payloadSeg}.{sig}";

        Assert.False(minter.Verify(token, "abcdefgh")); // missing pwd → rejected (TA-9.1: the claim set is exact)
    }

    [Fact]
    public void Mint_DifferentKeys_DifferentSignatures()
    {
        using var clock = new StubClock();
        var minterA = new UnlockTokenService(Key, clock.Fn);
        var minterB = new UnlockTokenService(Encoding.UTF8.GetBytes("different-key"), clock.Fn);

        var tokenA = minterA.Mint("abcdefgh");
        var tokenB = minterB.Mint("abcdefgh");

        Assert.False(minterA.Verify(tokenB, "abcdefgh")); // wrong key → signature mismatch (TA-9.1: same Wa:Jwt:Secret)
    }

    // ---- Helper: decode Base64Url to bytes (RFC 7515 §2.1 — URL-safe alphabet, no padding) ----

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded) =>
        Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));

    /// <summary>Flip the least significant bit of a character for tampering tests (deterministic single-bit flip).</summary>
    private static char FlipLastBit(char c) => (char)((int)c ^ 1);
}
