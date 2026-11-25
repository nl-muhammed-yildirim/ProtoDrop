namespace wa.application.UseCases.Transfers;

/// <summary>A due Scheduled row from the scan (Id + due time only, so the query stays covered by IX_Transfer_Sched).</summary>
public sealed record DueScheduledRow(Guid Id, DateTime ScheduledSendAtUtc);
