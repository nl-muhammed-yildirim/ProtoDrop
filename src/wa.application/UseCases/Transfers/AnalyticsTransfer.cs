namespace wa.application.UseCases.Transfers;

/// <summary>The owner-scoped transfer facts the handler needs (window start + status).</summary>
/// <param name="Id">Identifier of the analytics transfer.</param>
/// <param name="CreatedAtUtc">The created at UTC of the analytics transfer.</param>
/// <param name="Status">The status of the analytics transfer.</param>
public sealed record AnalyticsTransfer(Guid Id, DateTime CreatedAtUtc, byte Status);
