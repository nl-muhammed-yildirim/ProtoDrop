using wa.domain.Privacy;

namespace wa.domain;

/// <summary>A file transfer. TA-3.2: dbo.Transfer.</summary>
public class Transfer
{
    /// <summary>Primary key of the transfer.</summary>
    public Guid Id { get; set; }
    /// <summary>8-char unique link ID (TA-7.1).</summary>
    public string LinkId { get; set; } = null!;
    /// <summary>The sending user; null for anonymous transfers and for transfers re-homed after account deletion.</summary>
    public Guid? OwnerAppUserId { get; set; }
    /// <summary>0=Draft, 1=Active, 2=Expired, 3=DownloadLimit, 4=Deleted, 5=Scheduled (F-PRF-002); see <see cref="TransferStatus"/>.</summary>
    public byte Status { get; set; }
    /// <summary>UTC instant after which the transfer is no longer downloadable; stamped at send time from the plan's retention.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Download cap stamped at send time; -1 means unlimited.</summary>
    public int MaxDownloads { get; set; }
    /// <summary>Number of downloads served so far; compared with <see cref="MaxDownloads"/>.</summary>
    public int DownloadsCount { get; set; }
    /// <summary>Password hash (VARCHAR(256)); plain password never persisted.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>Sender name shown to recipients; PII (TA-9.4).</summary>
    [Pii]
    public string SenderName { get; set; } = null!;
    /// <summary>Sender email used for reply and unsubscribe handling; null for anonymous senders; PII (TA-9.4).</summary>
    [Pii]
    public string? SenderEmail { get; set; }
    /// <summary>Optional message from the sender shown on the download page.</summary>
    public string? Note { get; set; }
    /// <summary>UTC time at which a scheduled transfer becomes active and recipients are emailed; null for immediate sends.</summary>
    public DateTime? ScheduledSendAtUtc { get; set; }
    /// <summary>Sum of the file sizes in bytes.</summary>
    public long TotalBytes { get; set; }
    /// <summary>Number of files in the transfer.</summary>
    public int FileCount { get; set; }
    /// <summary>Self-reference: transfer that superseded this one (re-send).</summary>
    public Guid? SupersededBy { get; set; }
    /// <summary>The original transfer this row was re-sent from (T-010-01); null for a fresh draft. Read by SendStore.SaveSendAsync to set SupersededBy on the original.</summary>
    public Guid? SourcedFrom { get; set; }
    /// <summary>UTC instant the draft was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>UTC instant the expiry job actually marked the transfer expired; null while it has not run.</summary>
    public DateTime? ExpiredAtUtc { get; set; }
    /// <summary>Soft-delete (TA-3.1).</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
