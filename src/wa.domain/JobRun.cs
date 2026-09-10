namespace wa.domain;

public class JobRun
{
    public string JobKey { get; set; } = string.Empty;
    public DateTime RunAtUtc { get; set; }
    public DateTime LockUntilUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
}