namespace wa.domain;

/// <summary>Timer de-dup lock (TA-6.1). TA-3.2: dbo.JobRun — composite PK (JobKey, RunAtUtc).</summary>
public class JobRun
{
    /// <summary>Stable name of the timer job (first half of the composite key).</summary>
    public string JobKey { get; set; } = null!;
    /// <summary>Scheduled UTC tick this row represents; the second half of the key, so the same tick can only be claimed once.</summary>
    public DateTime RunAtUtc { get; set; }
    /// <summary>UTC instant until which other instances must not run the job; a crashed run is retried after this.</summary>
    public DateTime LockUntilUtc { get; set; }
    /// <summary>UTC instant the run completed; null while running or if it crashed.</summary>
    public DateTime? FinishedAtUtc { get; set; }
}
