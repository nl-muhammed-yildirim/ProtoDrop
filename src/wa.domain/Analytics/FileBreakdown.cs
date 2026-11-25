namespace wa.domain.Analytics;

/// <summary>FR-023-2/3: every file with its count (SortOrder), the separate download-all count (null = absent), top files.</summary>
/// <param name="Files">The files of the file breakdown.</param>
/// <param name="DownloadAll">The download all of the file breakdown.</param>
/// <param name="Top">The top of the file breakdown.</param>
public sealed record FileBreakdown(
    IReadOnlyList<FileDownloadCount> Files,
    int? DownloadAll,
    IReadOnlyList<FileDownloadCount> Top);
