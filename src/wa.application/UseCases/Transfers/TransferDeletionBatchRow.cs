namespace wa.application.UseCases.Transfers;

/// <summary>A row from the deletion scan — enough data to build the event envelope.</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="LinkId">Public link id, used as the event partition key.</param>
public sealed record TransferDeletionBatchRow(Guid Id, string LinkId);
