namespace wa.domain;

/// <summary>A ref-counted blob. TA-3.2: dbo.BlobRef.</summary>
public class BlobRef
{
    public Guid Id { get; set; }
    /// <summary>Blob path (unique).</summary>
    public string BlobPath { get; set; } = null!;
    public long SizeBytes { get; set; }
    public int RefCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Set when the blob is queued for physical deletion (IX_BlobRef_Cleanup).</summary>
    public DateTime? PhysicallyDeletedAtUtc { get; set; }
}
