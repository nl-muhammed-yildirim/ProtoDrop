namespace wa.domain.Analytics;

/// <summary>F-PRF-003: pure analytics functions (window, zero fill, top-N merge). All date logic takes <c>now</c> for testability.</summary>
public static class DownloadAnalytics
{
    /// <summary>Bucket key for a missing / invalid country.</summary>
    public const string UnknownCountry = "??";
    /// <summary>Bucket key for a missing / unparseable browser family.</summary>
    public const string OtherBrowser = "Other";
    /// <summary>FR-023-1 "top 5" — a spec display constant, not a plan limit.</summary>
    public const int TopCount = 5;

    /// <summary>First day of the window: the transfer's own send date (UTC day). Never the viewer's current plan — a transfer
    /// lives R + G days from SEND, so day-0 events must survive the last grace day and plan downgrades (FR-023-1).</summary>
    /// <param name="createdUtc">UTC instant the transfer was sent.</param>
    /// <returns>The UTC calendar day of <paramref name="createdUtc"/>.</returns>
    public static DateOnly WindowStart(DateTime createdUtc) => DateOnly.FromDateTime(createdUtc);

    /// <summary>Last day of the window: min(today, send date + retention + grace). The R + G cap only bounds the upper reach
    /// (chart length at most R + G + 1 days: send day through the deletion-job day).</summary>
    /// <param name="nowUtc">Current UTC instant (injected so the logic is testable).</param>
    /// <param name="createdUtc">UTC instant the transfer was sent.</param>
    /// <param name="retentionDays">Retention period R of the owner's plan, in days.</param>
    /// <param name="graceDays">Grace period G after retention, in days.</param>
    /// <returns>The earlier of today and the send day plus R + G days (negative sums are treated as zero).</returns>
    public static DateOnly WindowEnd(DateTime nowUtc, DateTime createdUtc, int retentionDays, int graceDays)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var cap = DateOnly.FromDateTime(createdUtc).AddDays(Math.Max(0, retentionDays + graceDays));
        return today < cap ? today : cap;
    }

    /// <summary>One entry per day from..to inclusive, ascending, zeros filled; empty when to &lt; from.</summary>
    /// <param name="sparse">Per-day counts from the store; days without events are absent and duplicate days are summed.</param>
    /// <param name="from">First day of the window (inclusive).</param>
    /// <param name="to">Last day of the window (inclusive).</param>
    /// <returns>A dense, ascending list with exactly one item per day of the window.</returns>
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
    /// <param name="raw">Grouped (country code, count) pairs as read from the store; keys may be null, blank or malformed.</param>
    /// <returns>Up to <see cref="TopCount"/> buckets, ordered by count descending then key ascending.</returns>
    public static IReadOnlyList<BucketCount> TopCountries(IEnumerable<(string? Key, int Count)> raw) =>
        Top(raw, NormalizeCountry);

    /// <summary>EC-023-3: null / blank → "Other"; merged, same order and cut.</summary>
    /// <param name="raw">Grouped (browser family, count) pairs as read from the store; keys may be null or blank.</param>
    /// <returns>Up to <see cref="TopCount"/> buckets, ordered by count descending then key ascending.</returns>
    public static IReadOnlyList<BucketCount> TopBrowsers(IEnumerable<(string? Key, int Count)> raw) =>
        Top(raw, k => string.IsNullOrWhiteSpace(k) ? OtherBrowser : k.Trim());

    /// <summary>Assembles the analytics result for one transfer: gap-filled daily line, top countries and browsers and the per-file breakdown. Total = sum of the daily counts in the window.</summary>
    /// <param name="sparseDaily">Per-day counts from the store; see <see cref="FillDaily"/>.</param>
    /// <param name="countries">Grouped (country code, count) pairs for <see cref="TopCountries"/>.</param>
    /// <param name="browsers">Grouped (browser family, count) pairs for <see cref="TopBrowsers"/>.</param>
    /// <param name="files">The transfer's files, used to list zero-count files too.</param>
    /// <param name="fileRows">SQL-grouped per-file counts; a null <c>FileId</c> means a download-all (zip) event.</param>
    /// <param name="from">First day of the window (inclusive).</param>
    /// <param name="to">Last day of the window (inclusive).</param>
    /// <returns>The aggregate analytics; never raw event rows.</returns>
    public static TransferAnalytics Build(
        IEnumerable<DailyCount> sparseDaily,
        IEnumerable<(string? Key, int Count)> countries,
        IEnumerable<(string? Key, int Count)> browsers,
        IEnumerable<AnalyticsFile> files,
        IEnumerable<FileCountRow> fileRows,
        DateOnly from,
        DateOnly to)
    {
        var daily = FillDaily(sparseDaily, from, to);
        var total = 0;
        foreach (var d in daily) total += d.Count;
        return new TransferAnalytics(total, daily, TopCountries(countries), TopBrowsers(browsers), FileCounts(files, fileRows));
    }

    /// <summary>
    /// FR-023-2/3: every file in SortOrder with its count (0 when none); download-all (FileId null) kept separate and
    /// absent (null) only for a single-file transfer with no zip events; top 5 = count desc, most recent event first,
    /// then SortOrder, then Id. Rows whose FileId matches no file (orphans) are ignored.
    /// </summary>
    /// <param name="files">The transfer's files with their sort order.</param>
    /// <param name="rows">SQL-grouped per-file counts and last-event times; a null <c>FileId</c> means a download-all (zip) event.</param>
    /// <returns>All files with counts, the download-all count (null when absent) and the top <see cref="TopCount"/> files.</returns>
    public static FileBreakdown FileCounts(IEnumerable<AnalyticsFile> files, IEnumerable<FileCountRow> rows)
    {
        var ordered = files.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToList();
        var counts = new Dictionary<Guid, int>();
        var last = new Dictionary<Guid, DateTime>();
        var zip = 0;
        foreach (var r in rows)
        {
            if (r.FileId is null) { zip += r.Count; continue; }
            counts[r.FileId.Value] = counts.GetValueOrDefault(r.FileId.Value) + r.Count;
            if (!last.TryGetValue(r.FileId.Value, out var l) || r.LastAtUtc > l) last[r.FileId.Value] = r.LastAtUtc;
        }

        var order = new Dictionary<Guid, int>();
        var list = new List<FileDownloadCount>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var f = ordered[i];
            order[f.Id] = i;
            list.Add(new FileDownloadCount(f.Id, f.Name, counts.GetValueOrDefault(f.Id)));
        }

        int? downloadAll = ordered.Count <= 1 && zip == 0 ? null : zip;
        var top = list.Where(f => f.Count > 0)
            .OrderByDescending(f => f.Count)
            .ThenByDescending(f => last.GetValueOrDefault(f.FileId))
            .ThenBy(f => order[f.FileId])
            .ThenBy(f => f.FileId)
            .Take(TopCount)
            .ToList();
        return new FileBreakdown(list, downloadAll, top);
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
