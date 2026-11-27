using wa.application.UseCases.Auth;

namespace wa.application.unit.Auth;

// CHORE-staging-blockers r2 (TA-9.1): the session epoch rule + the token's issue time.
public class SessionRevocationTests
{
    private static readonly DateTime Epoch = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TokenIssuedBeforeEpoch_IsRevoked() =>
        Assert.True(SessionRevocation.IsRevoked(Epoch.AddMilliseconds(-1), Epoch));

    [Fact]
    public void TokenIssuedAfterEpoch_IsAccepted() =>
        Assert.False(SessionRevocation.IsRevoked(Epoch.AddMilliseconds(1), Epoch));

    [Fact]
    public void TokenIssuedAtTheEpoch_IsAccepted() =>
        Assert.False(SessionRevocation.IsRevoked(Epoch, Epoch));

    [Fact]
    public void NullEpoch_AcceptsEveryToken() =>
        Assert.False(SessionRevocation.IsRevoked(Epoch.AddYears(-5), null));

    [Fact]
    public void SubMillisecondEpoch_DoesNotRejectASessionMintedRightAfterIt()
    {
        var epoch = Epoch.AddTicks(1234567); // DATETIME2 keeps 100 ns; the token's issue time has 1 ms resolution
        var issuedSameMs = Epoch.AddMilliseconds(epoch.Millisecond);
        Assert.False(SessionRevocation.IsRevoked(issuedSameMs, epoch));
    }

    [Fact]
    public void MintedToken_CarriesAnExplicitIssueTime_InMilliseconds()
    {
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, 123, TimeSpan.Zero);
        var svc = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("unit-test-session-key"), () => now);

        var parsed = svc.Parse(svc.Mint(Guid.NewGuid()));

        Assert.NotNull(parsed);
        Assert.Equal(now.UtcDateTime, parsed.Value.IssuedAtUtc);
    }

    [Fact]
    public void LegacyThreeSegmentToken_StillParses_WithIssueTimeDerivedFromExpiry()
    {
        var key = System.Text.Encoding.UTF8.GetBytes("unit-test-session-key");
        var svc = new SessionTokenService(key, () => DateTimeOffset.UtcNow);
        var userId = Guid.NewGuid();
        var exp = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds();
        var unsigned = $"{userId:N}|{exp}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsigned)));
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var parsed = svc.Parse(token);

        Assert.NotNull(parsed);
        Assert.Equal(parsed.Value.ExpUtc - TimeSpan.FromDays(SessionTokenService.LifetimeDays), parsed.Value.IssuedAtUtc);
    }

    [Fact]
    public void TamperedIssueTime_FailsTheSignature()
    {
        var svc = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("unit-test-session-key"), () => DateTimeOffset.UtcNow);
        var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(svc.Mint(Guid.NewGuid()))).Split('|');
        parts[2] = (long.Parse(parts[2]) + 10_000_000_000).ToString(); // an attacker back/forward-dating iat to dodge the epoch

        Assert.Null(svc.Parse(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join('|', parts)))));
    }
}
