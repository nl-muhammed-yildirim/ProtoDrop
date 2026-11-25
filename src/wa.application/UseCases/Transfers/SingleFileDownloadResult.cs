namespace wa.application.UseCases.Transfers;

/// <summary>Result of <see cref="IPublicTransferStore.RecordSingleFileDownloadAsync"/> — true iff the download was counted.</summary>
/// <param name="Outcome">The outcome of the single file download result.</param>
public sealed record SingleFileDownloadResult(SingleFileDownloadOutcome Outcome);
