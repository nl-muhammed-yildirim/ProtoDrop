namespace wa.domain;

/// <summary>Timer de-dup lock (TA-6.1). TA-3.2: dbo.JobRun — composite PK (JobKey, RunAtUtc).</summary>
public class JobRun
{
    /// <summary>Gets or sets the job key of the job run.</summary>
    public string JobKey { get; set; } = null!;
    /// <summary>Gets or sets the run at UTC of the job run.</summary>
    public DateTime RunAtUtc { get; set; }
    /// <summary>Gets or sets the lock until UTC of the job run.</summary>
    public DateTime LockUntilUtc { get; set; }
    /// <summary>Gets or sets the finished at UTC of the job run.</summary>
    public DateTime? FinishedAtUtc { get; set; }
}
