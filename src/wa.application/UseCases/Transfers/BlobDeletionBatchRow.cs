namespace wa.application.UseCases.Transfers;

/// <summary>A row from the blob deletion scan — enough data to delete the blob and remove the row.</summary>
/// <param name="Id">Primary key of the BlobRef row to remove after the blob is deleted.</param>
/// <param name="BlobPath">Canonical path of the blob to delete.</param>
public sealed record BlobDeletionBatchRow(Guid Id, string BlobPath);
