namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-022-04 (FR-022-5, TA-6.1): DB surface of the f-expire schedule sub-step — flips due Scheduled transfers to Active.
/// Implemented by ScheduledSendStore in wa.infrastructure.Persistence (fresh DbContext per call).
/// </summary>
public interface IScheduledSendStore
{
    /// <summary>Claim the 15-min window via JobRun (JobKey='schedule', RunAtUtc=trunc(now,15m)). Returns false if already claimed.</summary>
    Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct);

    /// <summary>Scan IX_Transfer_Sched for Status=5 AND ScheduledSendAtUtc &lt;= now, oldest due first, up to batchSize rows.</summary>
    Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct);

    /// <summary>
    /// Conditional flip Status 5 → 1 (only if still Scheduled and not expired). Returns the data for the transfer.created
    /// envelope when this call flipped the row, or null when it did not (expired, deleted, already flipped).
    /// </summary>
    Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Mark the JobRun as finished.</summary>
    Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct);
}

/// <summary>A due Scheduled row from the scan (Id + due time only, so the query stays covered by IX_Transfer_Sched).</summary>
public sealed record DueScheduledRow(Guid Id, DateTime ScheduledSendAtUtc);

/// <summary>Data needed to re-emit transfer.created after a Scheduled → Active flip (TA-5.3 payload shape).</summary>
public sealed record ActivatedTransfer(
    Guid Id,
    string LinkId,
    long TotalBytes,
    int FileCount,
    string? SenderEmail,
    DateTime ScheduledSendAtUtc,
    IReadOnlyList<string> Emails);
