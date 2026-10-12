namespace wa.domain;

/// <summary>Timer de-dup lock (TA-6.1). TA-3.2: dbo.JobRun — composite PK (JobKey, RunAtUtc).</summary>
public class JobRun
{
    public string JobKey { get; set; } = null!;
    public DateTime RunAtUtc { get; set; }
    public DateTime LockUntilUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}
