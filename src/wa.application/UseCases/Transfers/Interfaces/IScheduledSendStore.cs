namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-022-04 (FR-022-5, TA-6.1): DB surface of the f-expire schedule sub-step — flips due Scheduled transfers to Active.
/// Implemented by ScheduledSendStore in wa.infrastructure.Persistence (fresh DbContext per call).
/// </summary>
public interface IScheduledSendStore
{
    /// <summary>Claim the 15-min window via JobRun (JobKey='schedule', RunAtUtc=trunc(now,15m)). Returns false if already claimed.</summary>
    /// <param name="runWindowStartUtc">Start of the 15-minute window being claimed.</param>
    /// <param name="lockUntilUtc">UTC instant until which other instances must not run the job.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this instance claimed the window; <see langword="false"/> when another already did.</returns>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan IX_Transfer_Sched for Status=5 AND ScheduledSendAtUtc &lt;= now, oldest due first, up to batchSize rows.</summary>
    /// <param name="nowUtc">Current UTC time; scheduled transfers due at or before it are returned.</param>
    /// <param name="batchSize">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The due scheduled transfers, oldest due first.</returns>
    Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct);

    /// <summary>
    /// Conditional flip Status 5 → 1 (only if still Scheduled and not expired). Returns the data for the transfer.created
    /// envelope when this call flipped the row, or null when it did not (expired, deleted, already flipped).
    /// </summary>
    /// <param name="transferId">Id of the due transfer.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The data for the <c>transfer.created</c> event when this call flipped the row, otherwise <see langword="null"/>.</returns>
    Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    /// <param name="runWindowStartUtc">Start of the claimed window.</param>
    /// <param name="finishedAtUtc">UTC instant stored as the finish time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct);
}
