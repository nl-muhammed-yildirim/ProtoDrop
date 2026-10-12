namespace wa.domain;

/// <summary>A download analytics event. TA-3.2: dbo.DownloadEvent.</summary>
public class DownloadEvent
{
    /// <summary>BIGINT IDENTITY(1,1) — server-generated (not client).</summary>
    public long Id { get; set; }
    public Guid TransferId { get; set; }
    /// <summary>NULL = download-all.</summary>
    public Guid? FileId { get; set; }
    /// <summary>HMAC(ip) — PII-safe.</summary>
    public string? IpHash { get; set; }
    /// <summary>ISO country code (2 chars).</summary>
    public string? Country { get; set; }
    public string? UaHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
