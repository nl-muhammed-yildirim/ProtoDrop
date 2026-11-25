namespace wa.application.UseCases.Transfers;

/// <summary>A row from the expiry scan — enough data to build the event envelope.</summary>
/// <param name="Id">Identifier of the expiry batch row.</param>
/// <param name="LinkId">Identifier of the link of the expiry batch row.</param>
public sealed record ExpiryBatchRow(Guid Id, string LinkId);
