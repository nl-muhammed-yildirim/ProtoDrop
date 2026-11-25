namespace wa.domain.Analytics;

/// <summary>F-PRF-003 FR-023-1 result: aggregates only, never raw rows.</summary>
/// <param name="Total">Total download events in the window (sum of <paramref name="Daily"/>).</param>
/// <param name="Daily">Gap-filled daily download line, one item per UTC day.</param>
/// <param name="Countries">Top countries by downloads.</param>
/// <param name="Browsers">Top browser families by downloads.</param>
/// <param name="Files">Per-file breakdown including download-all.</param>
public sealed record TransferAnalytics(
    int Total,
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<BucketCount> Countries,
    IReadOnlyList<BucketCount> Browsers,
    FileBreakdown Files);
