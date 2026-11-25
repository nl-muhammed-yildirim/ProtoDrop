namespace wa.domain.Analytics;

/// <summary>F-PRF-003 FR-023-1 result: aggregates only, never raw rows.</summary>
public sealed record TransferAnalytics(
    int Total,
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<BucketCount> Countries,
    IReadOnlyList<BucketCount> Browsers,
    FileBreakdown Files);
