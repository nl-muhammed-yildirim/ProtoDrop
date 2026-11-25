namespace wa.application.UseCases.Transfers;

/// <summary>Result of recording a download against the transfer's cap.</summary>
/// <param name="Outcome">Whether the download was counted or the cap was already reached.</param>
public sealed record SingleFileDownloadResult(SingleFileDownloadOutcome Outcome);
