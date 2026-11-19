using wa.domain.Analytics;

namespace wa.domain.unit.Analytics;

/// <summary>US-023-01 (FR-023-1): pure window / zero-fill / top-N functions.</summary>
public class DownloadAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WindowStart_CreatedInsideWindow_ReturnsCreatedDate() =>
        Assert.Equal(new DateOnly(2026, 11, 10), DownloadAnalytics.WindowStart(Now, new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc), 30, 3));

    [Fact]
    public void WindowStart_OldTransfer_ReturnsTodayMinusRetentionPlusGraceMinusOne() =>
        Assert.Equal(new DateOnly(2026, 11, 19).AddDays(-32), DownloadAnalytics.WindowStart(Now, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 30, 3));

    [Fact]
    public void WindowStart_MidnightBoundary_LandsOnDifferentDays()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var before = DownloadAnalytics.WindowStart(new DateTime(2026, 11, 18, 23, 59, 59, DateTimeKind.Utc), created, 30, 3);
        var after = DownloadAnalytics.WindowStart(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), created, 30, 3);
        Assert.Equal(before.AddDays(1), after);
    }

    [Fact]
    public void FillDaily_FillsZeros_Ascending_InclusiveEnds()
    {
        var from = new DateOnly(2026, 11, 17);
        var to = new DateOnly(2026, 11, 19);
        var r = DownloadAnalytics.FillDaily([new DailyCount(to, 4), new DailyCount(from, 2)], from, to);

        Assert.Equal([new DailyCount(from, 2), new DailyCount(from.AddDays(1), 0), new DailyCount(to, 4)], r);
    }

    [Fact]
    public void FillDaily_ToBeforeFrom_Empty() =>
        Assert.Empty(DownloadAnalytics.FillDaily([], new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 18)));

    [Fact]
    public void TopCountries_InvalidValuesMergeIntoUnknown_ValidUpperCased()
    {
        var r = DownloadAnalytics.TopCountries([(null, 1), ("", 1), ("x", 1), ("nld", 1), ("??", 2), ("nl", 3)]);

        Assert.Equal([new BucketCount("??", 6), new BucketCount("NL", 3)], r);
    }

    [Fact]
    public void TopCountries_SevenKeys_CutToFive_TieBreaksByKey()
    {
        var raw = new (string? Key, int Count)[] { ("DE", 1), ("FR", 1), ("BE", 1), ("AT", 1), ("CH", 1), ("ES", 1), ("NL", 5) };

        var r = DownloadAnalytics.TopCountries(raw);

        Assert.Equal(5, r.Count);
        Assert.Equal(["NL", "AT", "BE", "CH", "DE"], r.Select(b => b.Key));
    }

    [Fact]
    public void TopBrowsers_NullMergesIntoOther()
    {
        var r = DownloadAnalytics.TopBrowsers([(null, 2), ("Other", 1), ("Chrome", 2)]);

        Assert.Equal([new BucketCount("Other", 3), new BucketCount("Chrome", 2)], r);
    }

    [Fact]
    public void Build_Total_IsSumOfDaily()
    {
        var from = new DateOnly(2026, 11, 17);
        var r = DownloadAnalytics.Build([new DailyCount(from, 2), new DailyCount(from.AddDays(2), 5)], [("NL", 7)], [("Chrome", 7)], from, from.AddDays(2));

        Assert.Equal(7, r.Total);
        Assert.Equal(3, r.Daily.Count);
    }
}
