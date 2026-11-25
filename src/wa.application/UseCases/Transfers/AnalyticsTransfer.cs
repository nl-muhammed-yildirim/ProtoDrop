namespace wa.application.UseCases.Transfers;

/// <summary>The owner-scoped transfer facts the handler needs (window start + status).</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="CreatedAtUtc">UTC instant the transfer was created; the first day of the analytics window.</param>
/// <param name="Status">Stored transfer status byte (see <c>TransferStatus</c>).</param>
public sealed record AnalyticsTransfer(Guid Id, DateTime CreatedAtUtc, byte Status);
