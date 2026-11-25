namespace wa.application.UseCases.Transfers;

/// <summary>A row from the blob deletion scan — enough data to delete the blob and remove the row.</summary>
public sealed record BlobDeletionBatchRow(Guid Id, string BlobPath);
