using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-02: minimal DB surface for the f-delete-transfers timer job (TA-6.4).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IExpiryDeletionStore
{
    /// <summary>Claim the 15-min window via JobRun composite PK (JobKey='delete-transfers', RunAtUtc=trunc(now,15m)). Returns false if another instance already claimed.</summary>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan for transfers with Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS &lt; cutoff. Returns up to batchSize rows ordered by ExpiredAtUtc ASC.</summary>
    Task<IReadOnlyList<TransferDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>In a single transaction: set Status=4, DeletedAtUtc=@now for the given transfer IDs; decrement BlobRef.RefCount per FileItem; delete FileItem + EmailRecipient rows.
    /// When a BlobRef hits RefCount=0, set PhysicallyDeletedAtUtc = now + 24h (EC-005-4 buffer). Returns rows actually updated.</summary>
    Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}

/// <summary>A row from the deletion scan — enough data to build the event envelope.</summary>
public sealed record TransferDeletionBatchRow(Guid Id, string LinkId);