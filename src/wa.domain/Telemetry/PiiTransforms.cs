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
    /// <param name="fileName">Original file name to anonymize.</param>
    /// <returns>64-character lowercase SHA-256 hex of the UTF-8 file name.</returns>
    public static string FileName(string fileName) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileName))).ToLowerInvariant();

    /// <summary>IP → lowercase HMAC-SHA256 hex keyed by the given key bytes (TA-9.4; production passes UTF-8 of <c>Wa:Jwt:Secret</c>).
    /// Keyed so one shared hash function doesn't expose IPs to anyone who knows the algorithm but not the key.</summary>
    /// <param name="ip">Client IP address as text.</param>
    /// <param name="hmacKey">Secret key bytes; the same key must be used everywhere so hashes stay comparable.</param>
    /// <returns>64-character lowercase HMAC-SHA256 hex.</returns>
    public static string IpAddress(string ip, byte[] hmacKey)
    {
        using var hmac = new HMACSHA256(hmacKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ip))).ToLowerInvariant();
    }

    /// <summary>User-Agent → lowercase HMAC-SHA256 hex (64 chars) keyed by the SAME key as <see cref="IpAddress"/> (TA-9.3/TA-9.4) —
    /// matches the DownloadEvent.UaHash column comment (HMAC(UA)).</summary>
    /// <param name="userAgent">Raw User-Agent header value.</param>
    /// <param name="hmacKey">Secret key bytes shared with <see cref="IpAddress"/>.</param>
    /// <returns>64-character lowercase HMAC-SHA256 hex.</returns>
    public static string UserAgent(string userAgent, byte[] hmacKey) => IpAddress(userAgent, hmacKey);
}
