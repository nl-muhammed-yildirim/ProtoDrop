namespace wa.domain.Analytics;

/// <summary>FR-023-2/3: every file with its count (SortOrder), the separate download-all count (null = absent), top files.</summary>
/// <param name="Files">Every file of the transfer in SortOrder, including files with zero downloads.</param>
/// <param name="DownloadAll">Number of download-all (zip) events; null when the transfer has a single file and no zip events.</param>
/// <param name="Top">The most downloaded files (at most five, non-zero only), ordered by count then recency.</param>
public sealed record FileBreakdown(
    IReadOnlyList<FileDownloadCount> Files,
    int? DownloadAll,
    IReadOnlyList<FileDownloadCount> Top);
