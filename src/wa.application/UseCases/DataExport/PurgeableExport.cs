namespace wa.application.UseCases.DataExport;

/// <summary>A ready export past retention: the row id and the blob to delete.</summary>
/// <param name="Id">Primary key of the export row to delete.</param>
/// <param name="BlobPath">Blob path of the zip to delete first.</param>
public sealed record PurgeableExport(Guid Id, string BlobPath);
