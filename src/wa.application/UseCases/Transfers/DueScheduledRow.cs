namespace wa.application.UseCases.Transfers;

/// <summary>A due Scheduled row from the scan (Id + due time only, so the query stays covered by IX_Transfer_Sched).</summary>
/// <param name="Id">Identifier of the due scheduled row.</param>
/// <param name="ScheduledSendAtUtc">The scheduled send at UTC of the due scheduled row.</param>
public sealed record DueScheduledRow(Guid Id, DateTime ScheduledSendAtUtc);
