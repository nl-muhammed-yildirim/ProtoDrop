namespace wa.domain.Analytics;

/// <summary>F-PRF-003 FR-023-1 result: aggregates only, never raw rows.</summary>
/// <param name="Total">The total of the transfer analytics.</param>
/// <param name="Daily">The daily of the transfer analytics.</param>
/// <param name="Countries">The countries of the transfer analytics.</param>
/// <param name="Browsers">The browsers of the transfer analytics.</param>
/// <param name="Files">The files of the transfer analytics.</param>
public sealed record TransferAnalytics(
    int Total,
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<BucketCount> Countries,
    IReadOnlyList<BucketCount> Browsers,
    FileBreakdown Files);
