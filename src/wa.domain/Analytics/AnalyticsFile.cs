namespace wa.domain.Analytics;

/// <summary>A transfer's file as the per-file breakdown input.</summary>
/// <param name="Id">Identifier of the analytics file.</param>
/// <param name="Name">The name of the analytics file.</param>
/// <param name="SortOrder">The sort order of the analytics file.</param>
public sealed record AnalyticsFile(Guid Id, string Name, int SortOrder);
