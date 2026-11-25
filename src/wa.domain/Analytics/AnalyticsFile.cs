namespace wa.domain.Analytics;

/// <summary>A transfer file as the input of the per-file download breakdown; only the fields the analytics need.</summary>
/// <param name="Id">File identifier; matches <c>FileCountRow.FileId</c> and is the final sort tie-break.</param>
/// <param name="Name">Original file name shown next to the count.</param>
/// <param name="SortOrder">Position of the file within the transfer; the breakdown lists files in this order.</param>
public sealed record AnalyticsFile(Guid Id, string Name, int SortOrder);
