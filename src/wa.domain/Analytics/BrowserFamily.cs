namespace wa.domain.Analytics;

/// <summary>
/// F-PRF-003 (EC-023-3): maps a raw User-Agent to a closed set of browser families.
/// Hand-rolled substring rules (no UA-parser package). Never throws; the raw UA is never persisted.
/// </summary>
public static class BrowserFamily
{
    public const string Edge = "Edge";
    public const string Opera = "Opera";
    public const string Firefox = "Firefox";
    public const string Chrome = "Chrome";
    public const string Safari = "Safari";
    public const string Other = "Other";

    /// <summary>The closed set of values <see cref="FromUserAgent"/> can return (besides null).</summary>
    public static readonly IReadOnlyList<string> All = [Edge, Opera, Firefox, Chrome, Safari, Other];

    /// <summary>Null/blank UA → null; otherwise one of <see cref="All"/>.</summary>
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
