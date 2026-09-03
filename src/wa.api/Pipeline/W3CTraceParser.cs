using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace wa.api.Pipeline;

/// <summary>
/// W3C <c>traceparent</c> helpers (TA-4.1.3): version <c>00</c>, 32-hex
/// trace-id, 16-hex span-id, 2-hex flags. Incoming values are validated and
/// normalized to lowercase; a fresh one (sampled flag <c>01</c>) is
/// generated when absent or invalid.
/// </summary>
internal static class W3CTraceParser
{
    public const string HeaderName = "traceparent";

    private static readonly Regex Pattern = new(
        @"^[00-0f]{2}-(?<trace>[0-9a-f]{32})-(?<span>[0-9a-f]{16})-[0-9a-f]{2}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Returns the validated, lowercase incoming <c>traceparent</c>,
    /// or <c>null</c> when missing/malformed (caller may generate).
    /// </summary>
    public static string? Parse(string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return null;
        }

        // A client may send a comma-separated list; only the first is honored.
        string value = incoming.Split(',', 2)[0].Trim();
        Match match = Pattern.Match(value);
        if (!match.Success)
        {
            return null;
        }

        string traceId = match.Groups["trace"].Value;
        if (AllZeros(traceId))
        {
            return null;
        }

        return value.ToLowerInvariant();
    }

    /// <summary>Generates a well-formed <c>traceparent</c> (sampled).</summary>
    public static string Generate()
    {
        Span<byte> random = stackalloc byte[24];
        RandomNumberGenerator.Fill(random);
        for (int i = 0; i < 16; i++)
        {
            if (random[i] == 0x00)
            {
                random[i] = 0x01;
            }

            if (random[i + 16] == 0x00)
            {
                random[i + 16] = 0x01;
            }
        }

        byte[] bytes = random.ToArray();
        string traceId = Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
        string spanId = Convert.ToHexString(bytes.AsSpan(16, 8)).ToLowerInvariant();
        return $"00-{traceId}-{spanId}-01";
    }

    /// <summary>
    /// The first 8 lowercase hex characters of the 32-hex trace-id
    /// (TA-10.1 correlation id).
    /// </summary>
    public static string CorrelationIdOf(string traceParent)
        => traceParent.Length >= 11
            ? traceParent.AsSpan(3, 8).ToString()
            : string.Empty;

    private static bool AllZeros(string s)
    {
        foreach (char c in s)
        {
            if (c != '0')
            {
                return false;
            }
        }

        return true;
    }
}
