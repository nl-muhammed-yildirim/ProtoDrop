using System.Security.Cryptography;
using System.Text;

namespace wa.infrastructure.Events;

/// <summary>
/// CHORE-local-email (TA-5.2, TA-6.2): the shared contract of the local-only email hand-off. The API (HttpHandOffEventPublisher)
/// and the workers (f-email-handoff) both use these constants and the HMAC helpers, so the header, keys, route and signature
/// live in one place. Used only in Local/Development; production delivers through Service Bus.
/// </summary>
public static class EmailHandOff
{
    /// <summary>Configuration key for the workers base URL the API posts events to.</summary>
    public const string BaseUrlKey = "Wa:EmailFunction:BaseUrl";

    /// <summary>Configuration key for the shared secret that signs the hand-off body (same value on API and workers).</summary>
    public const string SignKeyKey = "Wa:Email:HandOffSignKey";

    /// <summary>Name of the request header that carries the lowercase-hex HMAC-SHA256 of the body.</summary>
    public const string HeaderName = "x-email-sign";

    /// <summary>Route of the workers HTTP trigger (without the host's route prefix).</summary>
    public const string Route = "events/email";

    /// <summary>Path the API posts to, relative to the workers base URL: the Functions default route prefix <c>api</c> plus <see cref="Route"/>.</summary>
    public const string RelativeUrl = "api/events/email";

    /// <summary>Computes the signature of a hand-off body.</summary>
    /// <param name="body">The exact request body bytes.</param>
    /// <param name="key">The shared signing key (UTF-8).</param>
    /// <returns>Lowercase hex of HMAC-SHA256 over <paramref name="body"/>.</returns>
    public static string Sign(ReadOnlySpan<byte> body, string key) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body));

    /// <summary>Checks a provided signature against the body with a constant-time comparison.</summary>
    /// <param name="body">The exact request body bytes.</param>
    /// <param name="provided">The header value the caller sent.</param>
    /// <param name="key">The configured signing key.</param>
    /// <returns><see langword="true"/> when both the key and the signature are present and the signature matches; otherwise <see langword="false"/>.</returns>
    public static bool Verify(byte[] body, string? provided, string? key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(body, key)),
            Encoding.UTF8.GetBytes(provided));
    }
}
