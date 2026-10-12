namespace wa.domain;

/// <summary>A file transfer. TA-3.2: dbo.Transfer.</summary>
public class Transfer
{
    public Guid Id { get; set; }
    /// <summary>8-char unique link ID (TA-7.1).</summary>
    public string LinkId { get; set; } = null!;
    public Guid? OwnerAppUserId { get; set; }
    /// <summary>0=Draft, 1=Active, 2=Expired, 3=DownloadLimit, 4=Deleted.</summary>
    public byte Status { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public int MaxDownloads { get; set; }
    public int DownloadsCount { get; set; }
    /// <summary>Password hash (VARCHAR(256)); plain password never persisted.</summary>
    public string? PasswordHash { get; set; }
    public string SenderName { get; set; } = null!;
    public string? SenderEmail { get; set; }
    public string? Note { get; set; }
    public DateTime? ScheduledSendAtUtc { get; set; }
    public long TotalBytes { get; set; }
    public int FileCount { get; set; }
    /// <summary>Self-reference: transfer that superseded this one (re-send).</summary>
    public Guid? SupersededBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiredAtUtc { get; set; }
    /// <summary>Soft-delete (TA-3.1).</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
