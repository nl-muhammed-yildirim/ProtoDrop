namespace wa.domain.Correlation;

/// <summary>Validates incoming W3C <c>traceparent</c> headers (TA-10.1, FR-056-1). Malformed or wrong-version values are treated as absent (EC-056-1) — the pipeline never crashes on a bad header.</summary>
public static class W3CTraceParser
{
    /// <summary>The only version this API understands at M0. Higher versions need an explicit upgrade decision (TA-10.1).</summary>
    public const string SupportedVersion = "00";

    private const int TraceIdLength = 32; // 16 bytes hex
    private const int ParentIdLength = 16; // 8 bytes hex

    /// <summary>Returns the validated trace-id (32 lowercase hex chars), or <c>null</c> when the header is absent/malformed/wrong-version.</summary>
    /// <param name="traceparent">Raw <c>traceparent</c> header value; may be null or blank.</param>
    /// <returns>The lowercase 32-hex trace-id, or null when the header is absent, malformed, of the wrong version or all zeros.</returns>
    public static string? ParseTraceId(string? traceparent)
        => Parse(traceparent)?.TraceId;

    /// <summary>Parses a W3C <c>traceparent</c> value. Returns the full 4-field form (version zero-padded to "00", lowercase), or <c>null</c>.</summary>
    /// <param name="traceparent">Raw <c>traceparent</c> header value; may be null or blank.</param>
    /// <returns>The normalized <see cref="TraceParent"/>, or null when the value is absent or invalid (never throws).</returns>
    public static TraceParent? Parse(string? traceparent)
    {
        if (string.IsNullOrWhiteSpace(traceparent))
            return null;

        var parts = traceparent.Trim().Split('-'); // strict W3C form: exactly version-traceid-parentid-flags
        if (parts.Length != 4 || !IsAllHex(parts[0], 2) || parts[0] != SupportedVersion
            || !IsAllHex(parts[1], TraceIdLength) || !IsAllHex(parts[2], ParentIdLength)
            || !IsAllHex(parts[3], 2)) // trace-flags must be a 2-hex byte (TA-4.1.3-style strictness at M0)
            return null;

        var traceId = parts[1].ToLowerInvariant();
        if (traceId.All(c => c == '0')) // all-zero trace-id is invalid per W3C — treat as absent
            return null;

        return new TraceParent(parts[0], traceId, parts[2].ToLowerInvariant(), parts[3]);
    }

    /// <summary>Generates a fresh trace-id (16 random bytes) for requests without an inbound header.</summary>
    /// <returns>A 32-character lowercase hex string (a GUID without dashes).</returns>
    public static string NewTraceId() => Guid.NewGuid().ToString("n");

    /// <summary>Generates a fresh valid W3C traceparent (version "00", new 32-hex trace-id, new 16-hex parent-id, flags "07") for surfaces without an inbound header.</summary>
    /// <returns>A new, valid <see cref="TraceParent"/> with random trace and parent ids.</returns>
    public static TraceParent New() => new(SupportedVersion, NewTraceId(), Guid.NewGuid().ToString("n")[..ParentIdLength], "07");

    /// <summary>Returns true when the value is a valid W3C traceparent (version "00", 32-hex trace-id, 16-hex parent-id, 2-hex flags); false for null/empty/malformed/wrong-version/all-zero.</summary>
    /// <param name="value">Candidate <c>traceparent</c> header value; may be null.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> parses to a valid traceparent; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(string? value) => Parse(value) is not null;

    private static bool IsAllHex(string s, int length) =>
        s.Length == length && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
