namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-03: minimal DB surface for the f-delete-blobs timer job (TA-6.5).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IBlobDeletionStore
{
    /// <summary>Claim the hourly window via JobRun composite PK (JobKey='delete-blobs', RunAtUtc=trunc(now,1h)). Returns false if another instance already claimed.</summary>
    /// <param name="runWindowStartUtc">Start of the hourly window being claimed.</param>
    /// <param name="lockUntilUtc">UTC instant until which other instances must not run the job.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this instance claimed the window; <see langword="false"/> when another already did.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Select BlobRef rows with RefCount=0 AND PhysicallyDeletedAtUtc &lt; cutoffUtc. Returns up to batchSize rows ordered by PhysicallyDeletedAtUtc ASC.</summary>
    /// <param name="cutoffUtc">Only blobs queued for deletion before this instant are returned.</param>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The blob references that are ready to be physically deleted, oldest first.</returns>
    Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>Delete the BlobRef row by its ID (the blob was already deleted from storage).</summary>
    /// <param name="blobRefId">Primary key of the BlobRef row.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task DeleteRowAsync(Guid blobRefId, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">Start of the claimed window.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}
