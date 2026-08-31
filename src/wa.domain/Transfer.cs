namespace wa.domain;

public class Transfer
{
    public Guid Id { get; set; }
    public string LinkId { get; set; } = string.Empty;
    public Guid? OwnerAppUserId { get; set; }
    public byte Status { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int MaxDownloads { get; set; }
    public int DownloadsCount { get; set; }
    public string? PasswordHash { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public string? Note { get; set; }
    public DateTime? ScheduledSendAtUtc { get; set; }
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    public Guid? SupersededBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
