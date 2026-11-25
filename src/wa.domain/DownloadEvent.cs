namespace wa.domain;

/// <summary>A download analytics event. TA-3.2: dbo.DownloadEvent.</summary>
public class DownloadEvent
{
    /// <summary>BIGINT IDENTITY(1,1) — server-generated (not client).</summary>
    public long Id { get; set; }
    /// <summary>The transfer that was downloaded.</summary>
    public Guid TransferId { get; set; }
    /// <summary>NULL = download-all.</summary>
    public Guid? FileId { get; set; }
    /// <summary>HMAC of the client IP — the raw IP is never stored (PII-safe); null when unavailable.</summary>
    public string? IpHash { get; set; }
    /// <summary>Two-letter ISO country code of the downloader; nullable and not populated by the current code (analytics shows "??" when null).</summary>
    public string? Country { get; set; }
    /// <summary>Keyed hash (HMAC-SHA256) of the downloader's User-Agent, written on each recorded download; nullable, not read by the current code, and the raw value is never stored.</summary>
    public string? UaHash { get; set; }
    /// <summary>UA family at write time (F-PRF-003), never the raw UA.</summary>
    public string? UserAgentFamily { get; set; }
    /// <summary>UTC instant of the download; the analytics windows and daily buckets are derived from it.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
