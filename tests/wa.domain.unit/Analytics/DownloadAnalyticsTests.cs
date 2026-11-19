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
        var r = DownloadAnalytics.Build([new DailyCount(from, 2), new DailyCount(from.AddDays(2), 5)], [("NL", 7)], [("Chrome", 7)], [], [], from, from.AddDays(2));

        Assert.Equal(7, r.Total);
        Assert.Equal(3, r.Daily.Count);
    }

    private static readonly DateTime T0 = new(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);
    private static AnalyticsFile F(int n) => new(new Guid(n, 0, 0, new byte[8]), $"f{n}.txt", n);
    private static FileCountRow R(int n, int count, int minutes = 0) => new(new Guid(n, 0, 0, new byte[8]), count, T0.AddMinutes(minutes));

    [Fact]
    public void FileCounts_EveryFileListed_ZeroFilled_SortOrder()
    {
        var r = DownloadAnalytics.FileCounts([F(3), F(1), F(2)], [R(1, 4), R(3, 2)]);

        Assert.Equal(["f1.txt", "f2.txt", "f3.txt"], r.Files.Select(f => f.Name));
        Assert.Equal([4, 0, 2], r.Files.Select(f => f.Count));
    }

    [Fact]
    public void FileCounts_NullFileId_IsDownloadAll_NotInTop()
    {
        var r = DownloadAnalytics.FileCounts([F(1), F(2)], [R(1, 2), new FileCountRow(null, 9, T0)]);

        Assert.Equal(9, r.DownloadAll);
        Assert.Single(r.Top);
        Assert.Equal(F(1).Id, r.Top[0].FileId);
    }

    [Fact]
    public void FileCounts_SingleFile_NoZipEvents_DownloadAllNull()
    {
        var r = DownloadAnalytics.FileCounts([F(1)], [R(1, 3)]);

        Assert.Null(r.DownloadAll);
    }

    [Fact]
    public void FileCounts_MultiFile_NoZipEvents_DownloadAllZero()
    {
        var r = DownloadAnalytics.FileCounts([F(1), F(2)], []);

        Assert.Equal(0, r.DownloadAll);
        Assert.Empty(r.Top);
    }

    [Fact]
    public void FileCounts_TieOnCount_MostRecentFirst()
    {
        var r = DownloadAnalytics.FileCounts([F(1), F(2)], [R(1, 2, 0), R(2, 2, 30)]);

        Assert.Equal([F(2).Id, F(1).Id], r.Top.Select(f => f.FileId));
    }

    [Fact]
    public void FileCounts_FullTie_FallsBackToSortOrder()
    {
        var r = DownloadAnalytics.FileCounts([F(2), F(1)], [R(1, 2, 5), R(2, 2, 5)]);

        Assert.Equal([F(1).Id, F(2).Id], r.Top.Select(f => f.FileId));
    }

    [Fact]
    public void FileCounts_SevenFiles_TopCutToFive_ZeroCountsExcluded()
    {
        var files = Enumerable.Range(1, 7).Select(F).ToList();
        var rows = Enumerable.Range(1, 6).Select(n => R(n, n)).ToList();   // file 7 has zero

        var r = DownloadAnalytics.FileCounts(files, rows);

        Assert.Equal(5, r.Top.Count);
        Assert.Equal([6, 5, 4, 3, 2], r.Top.Select(f => f.Count));
        Assert.DoesNotContain(r.Top, f => f.Count == 0);
    }

    [Fact]
    public void FileCounts_OrphanFileId_Ignored()
    {
        var r = DownloadAnalytics.FileCounts([F(1), F(2)], [R(1, 1), R(99, 8)]);

        Assert.Equal(2, r.Files.Count);
        Assert.Single(r.Top);
    }

    [Fact]
    public void Build_CarriesFileBreakdown()
    {
        var from = new DateOnly(2026, 11, 17);
        var r = DownloadAnalytics.Build([], [], [], [F(1), F(2)], [R(2, 3), new FileCountRow(null, 1, T0)], from, from);

        Assert.Equal(2, r.Files.Files.Count);
        Assert.Equal(1, r.Files.DownloadAll);
        Assert.Equal(F(2).Id, r.Files.Top[0].FileId);
    }
}
