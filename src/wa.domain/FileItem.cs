namespace wa.domain;

/// <summary>A file belonging to a transfer. TA-3.2: dbo.FileItem.</summary>
public class FileItem
{
    /// <summary>Primary key of the file row.</summary>
    public Guid Id { get; set; }
    /// <summary>The transfer the file belongs to.</summary>
    public Guid TransferId { get; set; }
    /// <summary>The ref-counted blob holding the bytes; several files can share one blob.</summary>
    public Guid BlobRefId { get; set; }
    /// <summary>File name as uploaded by the sender; PII, so telemetry only sees its hash (TA-9.4).</summary>
    public string OriginalName { get; set; } = null!;
    /// <summary>File size in bytes.</summary>
    public long SizeBytes { get; set; }
    /// <summary>MIME type reported at upload, or null when unknown.</summary>
    public string? ContentType { get; set; }
    /// <summary>Zero-based position of the file within its transfer.</summary>
    public int SortOrder { get; set; }
}
