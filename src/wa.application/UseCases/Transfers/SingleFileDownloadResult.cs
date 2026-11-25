namespace wa.application.UseCases.Transfers;

/// <summary>Result of <see cref="IPublicTransferStore.RecordSingleFileDownloadAsync"/> — true iff the download was counted.</summary>
public sealed record SingleFileDownloadResult(SingleFileDownloadOutcome Outcome);
