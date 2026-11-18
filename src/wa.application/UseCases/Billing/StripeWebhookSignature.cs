using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-03 (FR-019-5, TA-4.2#19): pure Stripe-Signature verifier — BCL HMAC-SHA256, no I/O, no package.
/// Header: t=&lt;unix&gt;,v1=&lt;hex&gt;[,v1=&lt;hex&gt;][,v0=...]; signed payload = t + "." + raw body; key = UTF-8 of the endpoint secret.
/// </summary>
public static class StripeWebhookSignature
{
    /// <summary>Stripe's documented default replay tolerance (also the Stripe.net default).</summary>
    private const int ToleranceSeconds = 300;

    public static bool IsValid(string? header, string payload, string? secret, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(header) || payload is null)
            return false;

        long? timestamp = null;
        var candidates = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var value = kv[1].Trim();
            if (key == "t")
            {
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var t))
                    timestamp = t;
            }
            else if (key == "v1")
            {
                candidates.Add(value);
            }
        }

        if (timestamp is null || candidates.Count == 0)
            return false;

        if (Math.Abs(nowUtc.ToUnixTimeSeconds() - timestamp.Value) > ToleranceSeconds)
            return false;

        var signed = Encoding.UTF8.GetBytes(timestamp.Value.ToString(CultureInfo.InvariantCulture) + "." + payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = hmac.ComputeHash(signed);

        foreach (var candidate in candidates)
        {
            byte[] provided;
            try { provided = Convert.FromHexString(candidate); }
            catch (FormatException) { continue; }
            if (CryptographicOperations.FixedTimeEquals(expected, provided))
                return true;
        }
        return false;
    }
}
