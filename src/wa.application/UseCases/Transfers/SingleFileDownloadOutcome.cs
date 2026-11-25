namespace wa.application.UseCases.Transfers;

/// <summary>T-003-06 (TA-7.2 step 4) — the outcome of an atomic cap-checked increment.</summary>
public enum SingleFileDownloadOutcome
{
    /// <summary>The download was counted against the transfer's limit.</summary>
    Recorded,
    /// <summary>The maximum number of downloads was already reached, so nothing was counted and the download must be refused.</summary>
    CapReached
}
