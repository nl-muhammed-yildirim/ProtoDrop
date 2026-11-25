namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-01: minimal DB surface for the f-expire timer job (TA-6.3).
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface IExpiryStore
{
    /// <summary>Claim the 15-min window via JobRun composite PK (JobKey='expire', RunAtUtc=trunc(now,15m)). Returns false if another instance already claimed.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan IX_Transfer_Expiry for Status IN (1,5) AND ExpiresAtUtc &lt; cutoff. Returns up to batchSize rows ordered by ExpiresAtUtc ASC.</summary>
    /// <param name="cutoffUtc">The cutoff UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching expiry batch row items.</returns>
    Task<IReadOnlyList<ExpiryBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);

    /// <summary>In a single transaction: set Status=2, ExpiredAtUtc=@now for the given transfer IDs (only if still Status IN (1,5)). Returns rows actually updated.</summary>
    /// <param name="transferIds">Identifiers of the transfer.</param>
    /// <param name="expiredAtUtc">The expired at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The resulting numeric value.</returns>
    Task<int> MarkExpiredAsync(IReadOnlyList<Guid> transferIds, DateTime expiredAtUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct);
}
