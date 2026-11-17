namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-03: minimal DB surface for the f-delete-blobs timer job (TA-6.5).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IBlobDeletionStore
{
    /// <summary>Claim the hourly window via JobRun composite PK (JobKey='delete-blobs', RunAtUtc=trunc(now,1h)). Returns false if another instance already claimed.</summary>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Select BlobRef rows with RefCount=0 AND PhysicallyDeletedAtUtc &lt; cutoffUtc. Returns up to batchSize rows ordered by PhysicallyDeletedAtUtc ASC.</summary>
    Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>Delete the BlobRef row by its ID (the blob was already deleted from storage).</summary>
    Task DeleteRowAsync(Guid blobRefId, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}

/// <summary>A row from the blob deletion scan — enough data to delete the blob and remove the row.</summary>
public sealed record BlobDeletionBatchRow(Guid Id, string BlobPath);
