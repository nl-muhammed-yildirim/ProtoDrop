using System.Security.Cryptography;
using System.Text;

namespace wa.domain.Telemetry;

/// <summary>
/// The TA-9.4 PII transforms — file names → SHA-256, IPs → HMAC keyed by <c>Wa:Jwt:Secret</c>. Pure BCL functions (no packages):
/// the key bytes are passed in so this stays testable from wa.domain.unit; raw PII never appears in telemetry (TA-10.5).
/// </summary>
public static class PiiTransforms
{
    /// <summary>File name → lowercase SHA-256 hex (TA-9.4). Deterministic — the same file name always produces the same value.</summary>
    public static string FileName(string fileName) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileName))).ToLowerInvariant();

    /// <summary>IP → lowercase HMAC-SHA256 hex keyed by the given key bytes (TA-9.4; production passes UTF-8 of <c>Wa:Jwt:Secret</c>).
    /// Keyed so one shared hash function doesn't expose IPs to anyone who knows the algorithm but not the key.</summary>
    public static string IpAddress(string ip, byte[] hmacKey)
    {
        using var hmac = new HMACSHA256(hmacKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ip))).ToLowerInvariant();
    }
}
