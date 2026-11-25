namespace wa.application.UseCases.Transfers;

/// <summary>T-010-01: file item row projection (plain POCO — no EF entity in use case layer).</summary>
/// <param name="Id">File item id.</param>
/// <param name="BlobRefId">Blob the file points at.</param>
/// <param name="OriginalName">File name as uploaded.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="ContentType">MIME type, or null when unknown.</param>
/// <param name="SortOrder">Zero-based position within the transfer.</param>
public sealed record FileItemRow(Guid Id, Guid BlobRefId, string OriginalName, long SizeBytes, string? ContentType, int SortOrder);
