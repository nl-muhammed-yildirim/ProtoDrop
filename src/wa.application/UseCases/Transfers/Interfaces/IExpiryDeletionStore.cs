namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-02: minimal DB surface for the f-delete-transfers timer job (TA-6.4).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IExpiryDeletionStore
{
    /// <summary>Claim the 15-min window via JobRun composite PK (JobKey='delete-transfers', RunAtUtc=trunc(now,15m)). Returns false if another instance already claimed.</summary>
    /// <param name="runWindowStartUtc">Start of the 15-minute window being claimed.</param>
    /// <param name="lockUntilUtc">UTC instant until which other instances must not run the job.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this instance claimed the window; <see langword="false"/> when another already did.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan for transfers with Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS &lt; cutoff. Returns up to batchSize rows ordered by ExpiredAtUtc ASC.</summary>
    /// <param name="cutoffUtc">Transfers whose expiry plus the grace period is before this instant are returned.</param>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The transfers that are past their grace period, oldest first.</returns>
    Task<IReadOnlyList<TransferDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>In a single transaction: set Status=4, DeletedAtUtc=@now for the given transfer IDs; decrement BlobRef.RefCount per FileItem; delete FileItem + EmailRecipient rows and DownloadEvent rows (US-023-04, FR-023-6).
    /// When a BlobRef hits RefCount=0, set PhysicallyDeletedAtUtc = now + 24h (EC-005-4 buffer). Returns rows actually updated.</summary>
    /// <param name="transferIds">Ids of the transfers to delete.</param>
    /// <param name="deletedAtUtc">UTC instant stored as the deletion time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The number of transfers actually updated.</returns>
    Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">Start of the claimed window.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}
