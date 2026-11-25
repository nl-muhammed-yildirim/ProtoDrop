namespace wa.domain;

/// <summary>A download analytics event. TA-3.2: dbo.DownloadEvent.</summary>
public class DownloadEvent
{
    /// <summary>BIGINT IDENTITY(1,1) — server-generated (not client).</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets the transfer ID of the download event.</summary>
    public Guid TransferId { get; set; }
    /// <summary>NULL = download-all.</summary>
    public Guid? FileId { get; set; }
    /// <summary>HMAC(ip) — PII-safe.</summary>
    public string? IpHash { get; set; }
    /// <summary>ISO country code (2 chars).</summary>
    public string? Country { get; set; }
    /// <summary>Gets or sets the ua hash of the download event.</summary>
    public string? UaHash { get; set; }
    /// <summary>UA family at write time (F-PRF-003), never the raw UA.</summary>
    public string? UserAgentFamily { get; set; }
    /// <summary>Gets or sets the created at UTC of the download event.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
