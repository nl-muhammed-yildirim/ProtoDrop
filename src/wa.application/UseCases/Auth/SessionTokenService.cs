using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

public sealed class SessionTokenService : ISessionTokenMinter
{
    /// <summary>Frozen Session TTL — 30 days rolling (Open-Decisions-and-Constants.md Part 2, TA-9.1). Not a plan limit (TA-3.4).</summary>
    public const int LifetimeDays = 30;

    /// <summary>
    /// FR-008-2 / TA-9.1 rolling threshold: re-mint when remaining lifetime is under 15 days. Task instructions §2 cites TA-9.1's
    /// 30-day rolling rule; not a Part 2 FROZEN row → pinned as a named constant (no magic number).
    /// </summary>
    public const int RenewRemainingDays = 15;

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;

    public SessionTokenService(byte[] key, Func<DateTimeOffset> clock)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string Mint(Guid userId)
    {
        var now = _clock();
        var expSeconds = (now + TimeSpan.FromDays(LifetimeDays)).ToUnixTimeSeconds();
        return Sign(userId, expSeconds);
    }

    /// <summary>TA-9.1 PARSE: Base64 decode → 3-segment split → HMAC-SHA256 over userId|exp with FixedTimeEquals; expired/null on tampered.</summary>
    public (Guid UserId, DateTime ExpUtc)? Parse(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var str = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = str.Split('|');
            if (parts.Length != 3)
                return null;
            var userIdPart = parts[0];
            var expPart = parts[1];
            var sig = parts[2];

            // TA-9.1: HMAC-SHA256 over the first two segments, fixed-time compare (the exact logic T-008-02 lifts out of Program.cs).
            using var hmac = new HMACSHA256(_key);
            var expectedSig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{userIdPart}|{expPart}")));
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(sig), System.Text.Encoding.UTF8.GetBytes(expectedSig)))
                return null; // tampered signature → guest (AC-008-1 cookie round-trip)

            // Expiry is a Unix second (the wire form Program.cs' provisional reader used); no Guid/long parse errors leak.
            if (!Guid.TryParse(userIdPart, out var uid) || !long.TryParse(expPart, out var expUnix))
                return null;
            var expUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(expUnix);
            if (_clock().UtcDateTime > expUtc)
                return null; // expired → guest (AC-008-1 cookie round-trip)

            return (uid, expUtc);
        }
        catch
        {
            return null; // malformed base64 / decode failure → guest path (TA-9.1 — never throws from the middleware)
        }
    }

    /// <summary>FR-008-2 rolling refresh: re-mint when remaining lifetime is under RenewRemainingDays (the frozen threshold).</summary>
    public bool NeedsRenewal(DateTime expUtc, DateTime nowUtc) =>
        expUtc - nowUtc <= TimeSpan.FromDays(RenewRemainingDays);

    private string Sign(Guid userId, long expSeconds)
    {
        var unsigned = $"{userId:N}|{expSeconds}";
        using var hmac = new HMACSHA256(_key);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsigned)));
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }
}
