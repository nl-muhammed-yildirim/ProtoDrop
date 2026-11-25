namespace wa.application.UseCases.DataExport;

/// <summary>A ready export past retention: the row id and the blob to delete.</summary>
/// <param name="Id">Identifier of the purgeable export.</param>
/// <param name="BlobPath">The blob path of the purgeable export.</param>
public sealed record PurgeableExport(Guid Id, string BlobPath);
