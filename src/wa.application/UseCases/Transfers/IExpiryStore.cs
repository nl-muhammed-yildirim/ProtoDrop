using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-01: minimal DB surface for the f-expire timer job (TA-6.3).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IExpiryStore
{
    /// <summary>Claim the 15-min window via JobRun composite PK (JobKey='expire', RunAtUtc=trunc(now,15m)). Returns false if another instance already claimed.</summary>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan IX_Transfer_Expiry for Status=1 AND ExpiresAtUtc &lt; cutoff. Returns up to batchSize rows ordered by ExpiresAtUtc ASC.</summary>
    Task<IReadOnlyList<ExpiryBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>In a single transaction: set Status=2, ExpiredAtUtc=@now for the given transfer IDs (only if still Status=1). Returns rows actually updated.</summary>
    Task<int> MarkExpiredAsync(IReadOnlyList<Guid> transferIds, DateTime expiredAtUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}

/// <summary>A row from the expiry scan — enough data to build the event envelope.</summary>
public sealed record ExpiryBatchRow(Guid Id, string LinkId);
