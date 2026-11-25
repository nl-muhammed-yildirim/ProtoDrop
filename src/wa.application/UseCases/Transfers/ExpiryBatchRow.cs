namespace wa.application.UseCases.Transfers;

/// <summary>A row from the expiry scan — enough data to build the event envelope.</summary>
public sealed record ExpiryBatchRow(Guid Id, string LinkId);
