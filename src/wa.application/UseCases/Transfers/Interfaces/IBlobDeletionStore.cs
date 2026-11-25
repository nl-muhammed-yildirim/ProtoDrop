namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-03: minimal DB surface for the f-delete-blobs timer job (TA-6.5).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IBlobDeletionStore
{
    /// <summary>Claim the hourly window via JobRun composite PK (JobKey='delete-blobs', RunAtUtc=trunc(now,1h)). Returns false if another instance already claimed.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Select BlobRef rows with RefCount=0 AND PhysicallyDeletedAtUtc &lt; cutoffUtc. Returns up to batchSize rows ordered by PhysicallyDeletedAtUtc ASC.</summary>
    /// <param name="cutoffUtc">The cutoff UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching blob deletion batch row items.</returns>
    Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>Delete the BlobRef row by its ID (the blob was already deleted from storage).</summary>
    /// <param name="blobRefId">Identifier of the blob ref.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task DeleteRowAsync(Guid blobRefId, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}
