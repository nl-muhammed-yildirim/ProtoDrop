namespace wa.domain.Analytics;

/// <summary>One day bucket (UTC date) of the daily line.</summary>
public sealed record DailyCount(DateOnly Day, int Count);
