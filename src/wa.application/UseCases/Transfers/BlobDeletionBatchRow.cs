namespace wa.application.UseCases.Transfers;

/// <summary>A row from the blob deletion scan — enough data to delete the blob and remove the row.</summary>
/// <param name="Id">Identifier of the blob deletion batch row.</param>
/// <param name="BlobPath">The blob path of the blob deletion batch row.</param>
public sealed record BlobDeletionBatchRow(Guid Id, string BlobPath);
