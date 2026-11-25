using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace wa.application.UseCases.Transfers;

/// <summary>Default implementation of <see cref="IUnlockTokenMinter"/>.</summary>
public sealed class UnlockTokenService : IUnlockTokenMinter
{
    /// <summary>TA-9.1 literal "exp: now+7d" — a security constant (token lifetime), not a plan limit, so it is NOT a Limits Registry entry (TA-3.4).</summary>
    public const int LifetimeDays = 7;

    private static readonly byte[] HeaderBytes = Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"HS256\"}"); // frozen — Base64Url("eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9")

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Initializes a new instance of the <see cref="UnlockTokenService"/> class.</summary>
    /// <param name="key">The key.</param>
    /// <param name="clock">The clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public UnlockTokenService(byte[] key, Func<DateTimeOffset> clock)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public string Mint(string linkId)
    {
        var now = _clock();
        var payloadJson = JsonSerializer.Serialize(new
        {
            sub = linkId,
            pwd = true, // TA-9.1: the unlock marker — endpoints 6/7 accept tokens where this is true
            exp = (now + TimeSpan.FromDays(LifetimeDays)).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("n")
        });

        var headerSeg = Base64UrlEncode(HeaderBytes);
        var payloadSeg = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = headerSeg + "." + payloadSeg;
        return signingInput + "." + Base64UrlEncode(EncodeHmac(signingInput));
    }

    /// <inheritdoc />
    public bool Verify(string? token, string expectedSub)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var segments = token.Split('.');
        if (segments.Length != 3)
            return false;

        var signingInput = segments[0] + "." + segments[1];
        try
        {
            // Constant-time signature comparison — TA-9.6: a junk or tampered token behaves exactly like an absent one.
            if (!CryptographicOperations.FixedTimeEquals(EncodeHmac(signingInput), Base64UrlDecode(segments[2])))
                return false;
        }
        catch (FormatException) // non-Base64 signature segment
        {
            return false;
        }

        try
        {
            var payloadJson = Base64UrlDecode(segments[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sub", out var sub) || sub.GetString() != expectedSub)
                return false;
            if (!root.TryGetProperty("pwd", out var pwd) || pwd.GetBoolean() != true)
                return false;
            if (!root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expSeconds))
                return false;

            return expSeconds > _clock().ToUnixTimeSeconds(); // expired or equal → false (the 7-day life is hard)
        }
        catch (Exception) // malformed Base64, non-JSON payload, wrong-typed claims — all fail closed
        {
            return false;
        }
    }

    private byte[] EncodeHmac(string signingInput)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
    }

    /// <summary>Base64Url (RFC 7515 §2.1) — JWT's URL-safe alphabet, no padding.</summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string encoded) =>
        Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));
}
