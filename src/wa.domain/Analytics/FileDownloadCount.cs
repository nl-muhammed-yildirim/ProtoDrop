namespace wa.domain.Analytics;

/// <summary>One file with its download count.</summary>
/// <param name="FileId">Identifier of the file of the file download count.</param>
/// <param name="Name">The name of the file download count.</param>
/// <param name="Count">The count of the file download count.</param>
public sealed record FileDownloadCount(Guid FileId, string Name, int Count);
