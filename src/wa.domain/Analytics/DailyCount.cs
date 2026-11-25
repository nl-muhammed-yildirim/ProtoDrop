namespace wa.domain.Analytics;

/// <summary>One day bucket (UTC date) of the daily line.</summary>
/// <param name="Day">The day of the daily count.</param>
/// <param name="Count">The count of the daily count.</param>
public sealed record DailyCount(DateOnly Day, int Count);
