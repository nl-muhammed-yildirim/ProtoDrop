namespace wa.domain.Analytics;

/// <summary>One day bucket (UTC date) of the daily line.</summary>
public sealed record DailyCount(DateOnly Day, int Count);

/// <summary>A top-N bucket (country code or browser family) with its count.</summary>
public sealed record BucketCount(string Key, int Count);

/// <summary>F-PRF-003 FR-023-1 result: aggregates only, never raw rows.</summary>
public sealed record TransferAnalytics(
    int Total,
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<BucketCount> Countries,
    IReadOnlyList<BucketCount> Browsers);

/// <summary>F-PRF-003: pure analytics functions (window, zero fill, top-N merge). All date logic takes <c>now</c> for testability.</summary>
public static class DownloadAnalytics
{
    /// <summary>Bucket key for a missing / invalid country.</summary>
    public const string UnknownCountry = "??";
    /// <summary>Bucket key for a missing / unparseable browser family.</summary>
    public const string OtherBrowser = "Other";
    /// <summary>FR-023-1 "top 5" — a spec display constant, not a plan limit.</summary>
    public const int TopCount = 5;

    /// <summary>First day of the window: max(today - (retention + grace - 1) days, created date).</summary>
    public static DateOnly WindowStart(DateTime nowUtc, DateTime createdUtc, int retentionDays, int graceDays)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var span = Math.Max(1, retentionDays + graceDays);
        var floor = today.AddDays(-(span - 1));
        var created = DateOnly.FromDateTime(createdUtc);
        return created > floor ? created : floor;
    }

    /// <summary>One entry per day from..to inclusive, ascending, zeros filled; empty when to &lt; from.</summary>
    public static IReadOnlyList<DailyCount> FillDaily(IEnumerable<DailyCount> sparse, DateOnly from, DateOnly to)
    {
        if (to < from) return [];
        var byDay = new Dictionary<DateOnly, int>();
        foreach (var d in sparse)
            byDay[d.Day] = byDay.GetValueOrDefault(d.Day) + d.Count;

        var result = new List<DailyCount>(to.DayNumber - from.DayNumber + 1);
        for (var day = from; day <= to; day = day.AddDays(1))
            result.Add(new DailyCount(day, byDay.GetValueOrDefault(day)));
        return result;
    }

    /// <summary>EC-023-2: null / blank / not two ASCII letters → "??"; upper-cased, merged, count desc then key asc, top 5.</summary>
    public static IReadOnlyList<BucketCount> TopCountries(IEnumerable<(string? Key, int Count)> raw) =>
        Top(raw, NormalizeCountry);

    /// <summary>EC-023-3: null / blank → "Other"; merged, same order and cut.</summary>
    public static IReadOnlyList<BucketCount> TopBrowsers(IEnumerable<(string? Key, int Count)> raw) =>
        Top(raw, k => string.IsNullOrWhiteSpace(k) ? OtherBrowser : k.Trim());

    /// <summary>Total = sum of the daily counts in the window.</summary>
    public static TransferAnalytics Build(
        IEnumerable<DailyCount> sparseDaily,
        IEnumerable<(string? Key, int Count)> countries,
        IEnumerable<(string? Key, int Count)> browsers,
        DateOnly from,
        DateOnly to)
    {
        var daily = FillDaily(sparseDaily, from, to);
        var total = 0;
        foreach (var d in daily) total += d.Count;
        return new TransferAnalytics(total, daily, TopCountries(countries), TopBrowsers(browsers));
    }

    private static string NormalizeCountry(string? code)
    {
        var c = code?.Trim();
        if (c is { Length: 2 } && IsAsciiLetter(c[0]) && IsAsciiLetter(c[1]))
            return c.ToUpperInvariant();
        return UnknownCountry;
    }

    private static bool IsAsciiLetter(char ch) => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static IReadOnlyList<BucketCount> Top(IEnumerable<(string? Key, int Count)> raw, Func<string?, string> normalize)
    {
        var merged = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, count) in raw)
        {
            var k = normalize(key);
            merged[k] = merged.GetValueOrDefault(k) + count;
        }
        return merged
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(TopCount)
            .Select(kv => new BucketCount(kv.Key, kv.Value))
            .ToList();
    }
}
