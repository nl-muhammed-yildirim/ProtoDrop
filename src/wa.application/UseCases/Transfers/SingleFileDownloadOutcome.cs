namespace wa.application.UseCases.Transfers;

/// <summary>T-003-06 (TA-7.2 step 4) — the outcome of an atomic cap-checked increment.</summary>
public enum SingleFileDownloadOutcome
{
    /// <summary>Recorded.</summary>
    Recorded,
    /// <summary>Cap reached.</summary>
    CapReached
}
