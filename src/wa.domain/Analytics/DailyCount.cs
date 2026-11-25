namespace wa.domain.Analytics;

/// <summary>One day bucket (UTC date) of the daily download line.</summary>
/// <param name="Day">UTC calendar day of the bucket.</param>
/// <param name="Count">Number of download events on that day; zero for gap-filled days.</param>
public sealed record DailyCount(DateOnly Day, int Count);
