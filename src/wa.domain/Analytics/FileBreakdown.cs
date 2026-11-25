namespace wa.domain.Analytics;

/// <summary>FR-023-2/3: every file with its count (SortOrder), the separate download-all count (null = absent), top files.</summary>
public sealed record FileBreakdown(
    IReadOnlyList<FileDownloadCount> Files,
    int? DownloadAll,
    IReadOnlyList<FileDownloadCount> Top);
