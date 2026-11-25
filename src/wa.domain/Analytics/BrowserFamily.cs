namespace wa.domain.Analytics;

/// <summary>
/// F-PRF-003 (EC-023-3): maps a raw User-Agent to a closed set of browser families.
/// Hand-rolled substring rules (no UA-parser package). Never throws; the raw UA is never persisted.
/// </summary>
public static class BrowserFamily
{
    /// <summary>Browser-family bucket for Microsoft Edge (desktop, Android and iOS tokens).</summary>
    public const string Edge = "Edge";
    /// <summary>Browser-family bucket for Opera.</summary>
    public const string Opera = "Opera";
    /// <summary>Browser-family bucket for Firefox (desktop and iOS).</summary>
    public const string Firefox = "Firefox";
    /// <summary>Browser-family bucket for Chrome and Chrome on iOS; Edge and Opera are matched first.</summary>
    public const string Chrome = "Chrome";
    /// <summary>Browser-family bucket for Safari; only reached when no other family token matches.</summary>
    public const string Safari = "Safari";
    /// <summary>Fallback bucket for a non-blank user agent that matches no known family.</summary>
    public const string Other = "Other";

    /// <summary>The closed set of values <see cref="FromUserAgent"/> can return (besides null).</summary>
    public static readonly IReadOnlyList<string> All = [Edge, Opera, Firefox, Chrome, Safari, Other];

    /// <summary>Maps a raw User-Agent header to one of <see cref="All"/>; the match order matters because Edge and Opera also contain a Chrome token.</summary>
    /// <param name="userAgent">Raw User-Agent header; may be null or blank.</param>
    /// <returns>The browser family, or null when <paramref name="userAgent"/> is null or blank.</returns>
    public static string? FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        if (Has(userAgent, "Edg/") || Has(userAgent, "EdgA/") || Has(userAgent, "EdgiOS/")) return Edge;
        if (Has(userAgent, "OPR/") || Has(userAgent, "Opera")) return Opera;
        if (Has(userAgent, "Firefox/") || Has(userAgent, "FxiOS/")) return Firefox;
        if (Has(userAgent, "CriOS/") || Has(userAgent, "Chrome/")) return Chrome;
        if (Has(userAgent, "Safari/")) return Safari;
        return Other;
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
