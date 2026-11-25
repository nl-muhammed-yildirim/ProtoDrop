namespace wa.application.UseCases.Transfers;

/// <summary>The owner-scoped transfer facts the handler needs (window start + status).</summary>
public sealed record AnalyticsTransfer(Guid Id, DateTime CreatedAtUtc, byte Status);
