namespace wa.domain.Analytics;

/// <summary>A transfer's file as the per-file breakdown input.</summary>
public sealed record AnalyticsFile(Guid Id, string Name, int SortOrder);
