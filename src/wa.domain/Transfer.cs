namespace wa.domain;

/// <summary>A file transfer. TA-3.2: dbo.Transfer.</summary>
public class Transfer
{
    /// <summary>Gets or sets the ID of the transfer.</summary>
    public Guid Id { get; set; }
    /// <summary>8-char unique link ID (TA-7.1).</summary>
    public string LinkId { get; set; } = null!;
    /// <summary>Gets or sets the owner app user ID of the transfer.</summary>
    public Guid? OwnerAppUserId { get; set; }
    /// <summary>0=Draft, 1=Active, 2=Expired, 3=DownloadLimit, 4=Deleted, 5=Scheduled (F-PRF-002); see <see cref="TransferStatus"/>.</summary>
    public byte Status { get; set; }
    /// <summary>Gets or sets the expires at UTC of the transfer.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Gets or sets the max downloads of the transfer.</summary>
    public int MaxDownloads { get; set; }
    /// <summary>Gets or sets the downloads count of the transfer.</summary>
    public int DownloadsCount { get; set; }
    /// <summary>Password hash (VARCHAR(256)); plain password never persisted.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Gets or sets the sender name of the transfer.</summary>
    public string SenderName { get; set; } = null!;
    /// <summary>Gets or sets the sender email of the transfer.</summary>
    public string? SenderEmail { get; set; }
    /// <summary>Gets or sets the note of the transfer.</summary>
    public string? Note { get; set; }
    /// <summary>Gets or sets the scheduled send at UTC of the transfer.</summary>
    public DateTime? ScheduledSendAtUtc { get; set; }
    /// <summary>Gets or sets the total bytes of the transfer.</summary>
    public long TotalBytes { get; set; }
    /// <summary>Gets or sets the file count of the transfer.</summary>
    public int FileCount { get; set; }
    /// <summary>Self-reference: transfer that superseded this one (re-send).</summary>
    public Guid? SupersededBy { get; set; }
    /// <summary>The original transfer this row was re-sent from (T-010-01); null for a fresh draft. Read by SendStore.SaveSendAsync to set SupersededBy on the original.</summary>
    public Guid? SourcedFrom { get; set; }
    /// <summary>Gets or sets the created at UTC of the transfer.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the expired at UTC of the transfer.</summary>
    public DateTime? ExpiredAtUtc { get; set; }
    /// <summary>Soft-delete (TA-3.1).</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
