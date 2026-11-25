namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-02: minimal DB surface for the f-delete-transfers timer job (TA-6.4).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IExpiryDeletionStore
{
    /// <summary>Claim the 15-min window via JobRun composite PK (JobKey='delete-transfers', RunAtUtc=trunc(now,15m)). Returns false if another instance already claimed.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan for transfers with Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS &lt; cutoff. Returns up to batchSize rows ordered by ExpiredAtUtc ASC.</summary>
    /// <param name="cutoffUtc">The cutoff UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching transfer deletion batch row items.</returns>
    Task<IReadOnlyList<TransferDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>In a single transaction: set Status=4, DeletedAtUtc=@now for the given transfer IDs; decrement BlobRef.RefCount per FileItem; delete FileItem + EmailRecipient rows and DownloadEvent rows (US-023-04, FR-023-6).
    /// When a BlobRef hits RefCount=0, set PhysicallyDeletedAtUtc = now + 24h (EC-005-4 buffer). Returns rows actually updated.</summary>
    /// <param name="transferIds">Identifiers of the transfer.</param>
    /// <param name="deletedAtUtc">The deleted at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The resulting numeric value.</returns>
    Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}
