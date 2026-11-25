namespace wa.application.UseCases.Transfers;

/// <summary>A row from the expiry scan — enough data to build the event envelope.</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="LinkId">Public link id, used as the event partition key.</param>
public sealed record ExpiryBatchRow(Guid Id, string LinkId);
