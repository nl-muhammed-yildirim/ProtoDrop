namespace wa.domain.Analytics;

/// <summary>One file with its download count.</summary>
public sealed record FileDownloadCount(Guid FileId, string Name, int Count);
