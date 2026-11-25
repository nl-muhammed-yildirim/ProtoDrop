using System.Security.Cryptography;

namespace wa.application.UseCases.Auth;

/// <summary>Default implementation of <see cref="ISessionTokenMinter"/>.</summary>
public sealed class SessionTokenService : ISessionTokenMinter
{
    /// <summary>Lifetime of a session token in days (30, frozen in Open-Decisions-and-Constants Part 2 and TA-9.1); not a plan limit.</summary>
    public const int LifetimeDays = 30;

    /// <summary>A token with this many days or fewer left is re-issued on use, which keeps the 30-day session rolling (FR-008-2, TA-9.1).</summary>
    public const int RenewRemainingDays = 15;

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Initializes a new instance of the <see cref="SessionTokenService"/> class.</summary>
    /// <param name="key">HMAC signing key bytes; shared by every instance so tokens verify across nodes.</param>
    /// <param name="clock">Time source (injected for tests).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="clock"/> is null.</exception>
    public SessionTokenService(byte[] key, Func<DateTimeOffset> clock)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public string Mint(Guid userId)
    {
        var now = _clock();
        var expSeconds = (now + TimeSpan.FromDays(LifetimeDays)).ToUnixTimeSeconds();
        return Sign(userId, expSeconds);
    }

    /// <summary>Decodes a session token, verifies its HMAC-SHA256 signature in constant time and checks its expiry.</summary>
    /// <param name="token">Raw cookie value; may be null or empty.</param>
    /// <returns>The user id and UTC expiry, or <see langword="null"/> when the token is missing, malformed, tampered with or expired.</returns>
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

    /// <summary>Tells whether a token is within <see cref="RenewRemainingDays"/> of its expiry and should be re-issued.</summary>
    /// <param name="expUtc">Expiry of the current token.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns><see langword="true"/> when the remaining lifetime is at or below the renewal threshold; otherwise <see langword="false"/>.</returns>
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
