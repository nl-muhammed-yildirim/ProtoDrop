namespace wa.application.UseCases.Transfers;

/// <summary>A due Scheduled row from the scan (Id + due time only, so the query stays covered by IX_Transfer_Sched).</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="ScheduledSendAtUtc">The send time the transfer was scheduled for.</param>
public sealed record DueScheduledRow(Guid Id, DateTime ScheduledSendAtUtc);
