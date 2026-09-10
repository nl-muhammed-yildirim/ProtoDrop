namespace wa.domain;

public class DownloadEvent
{
    public long Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid? FileId { get; set; }
    public string? IpHash { get; set; }
    public string? Country { get; set; }
    public string? UaHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}