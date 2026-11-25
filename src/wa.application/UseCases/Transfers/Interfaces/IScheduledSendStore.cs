namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-022-04 (FR-022-5, TA-6.1): DB surface of the f-expire schedule sub-step — flips due Scheduled transfers to Active.
/// Implemented by ScheduledSendStore in wa.infrastructure.Persistence (fresh DbContext per call).
/// </summary>
public interface IScheduledSendStore
{
    /// <summary>Claim the 15-min window via JobRun (JobKey='schedule', RunAtUtc=trunc(now,15m)). Returns false if already claimed.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="lockUntilUtc">The lock until UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the try claim condition holds; otherwise <see langword="false"/>.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan IX_Transfer_Sched for Status=5 AND ScheduledSendAtUtc &lt;= now, oldest due first, up to batchSize rows.</summary>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching due scheduled row items.</returns>
    Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct);

    /// <summary>
    /// Conditional flip Status 5 → 1 (only if still Scheduled and not expired). Returns the data for the transfer.created
    /// envelope when this call flipped the row, or null when it did not (expired, deleted, already flipped).
    /// </summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The activated transfer, or <see langword="null"/> when there is none.</returns>
    Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">The run window start UTC.</param>
    /// <param name="finishedAtUtc">The finished at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct);
}
