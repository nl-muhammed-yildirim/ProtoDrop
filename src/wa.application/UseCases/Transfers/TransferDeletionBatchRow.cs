namespace wa.application.UseCases.Transfers;

/// <summary>A row from the deletion scan — enough data to build the event envelope.</summary>
/// <param name="Id">Identifier of the transfer deletion batch row.</param>
/// <param name="LinkId">Identifier of the link of the transfer deletion batch row.</param>
public sealed record TransferDeletionBatchRow(Guid Id, string LinkId);
