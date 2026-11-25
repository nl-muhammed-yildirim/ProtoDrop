namespace wa.application.UseCases.Transfers;

/// <summary>A row from the deletion scan — enough data to build the event envelope.</summary>
public sealed record TransferDeletionBatchRow(Guid Id, string LinkId);
