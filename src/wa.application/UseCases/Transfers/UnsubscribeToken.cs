using System.Security.Cryptography;
using System.Text;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-006-03 (TA-6.7): Unsubscribe token — Base64Url(payload).Base64Url(HMACSHA256(payload, key)).
/// Payload = "address|senderEmail" (UTF-8). BCL-only (HMACSHA256 + Base64), same style as UnlockToken.cs.
/// No JWT header/exp — just payload+sig (the token is stateless and doesn't expire; the suppression row is permanent).
/// </summary>
public static class UnsubscribeToken
{
    /// <summary>Create a token for the given address + senderEmail pair.</summary>
    public static string Create(string address, string senderEmail, byte[] hmacKey)
    {
        var payload = $"{address}|{senderEmail}";
        var encodedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        using var hmac = new HMACSHA256(hmacKey);
        var sig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedPayload)));
        return $"{encodedPayload}.{sig}";
    }

    /// <summary>Verify a token and return the (address, senderEmail) pair. Returns null on tamper/parse failure.</summary>
    public static (string Address, string SenderEmail)? Verify(string? token, byte[] hmacKey)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var segments = token.Split('.', 2);
        if (segments.Length != 2)
            return null;

        try
        {
            // Recompute HMAC over the payload segment.
            using var hmac = new HMACSHA256(hmacKey);
            var expectedSig = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(segments[0])));

            // Constant-time comparison — TA-9.6: tampered token behaves exactly like an unknown one (no timing leak).
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(segments[1])))
                return null;

            var payloadBytes = Base64UrlDecode(segments[0]);
            var payload = Encoding.UTF8.GetString(payloadBytes);

            var sepIdx = payload.IndexOf('|');
            if (sepIdx <= 0 || sepIdx == payload.Length - 1)
                return null;

            var address = payload[..sepIdx];
            var senderEmail = payload[(sepIdx + 1)..];

            return (address, senderEmail);
        }
        catch (FormatException) // non-Base64 segment
        {
            return null;
        }
    }

    /// <summary>Base64Url encode (RFC 7515 §2.1) — URL-safe alphabet, no padding.</summary>
    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Base64Url decode (RFC 7515 §2.1).</summary>
    private static byte[] Base64UrlDecode(string encoded) =>
        Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));
}
