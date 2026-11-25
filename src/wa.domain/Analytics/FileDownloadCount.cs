namespace wa.domain.Analytics;

/// <summary>One file with its download count.</summary>
/// <param name="FileId">Identifier of the transfer file.</param>
/// <param name="Name">Original file name for display.</param>
/// <param name="Count">Number of download events for the file; zero when never downloaded.</param>
public sealed record FileDownloadCount(Guid FileId, string Name, int Count);
