namespace wa.domain;

/// <summary>A ref-counted blob. TA-3.2: dbo.BlobRef.</summary>
public class BlobRef
{
    /// <summary>Primary key of the blob reference.</summary>
    public Guid Id { get; set; }
    /// <summary>Blob path (unique).</summary>
    public string BlobPath { get; set; } = null!;
    /// <summary>Size of the stored blob in bytes.</summary>
    public long SizeBytes { get; set; }
    /// <summary>Number of file items currently pointing at the blob; the blob becomes deletable at zero.</summary>
    public int RefCount { get; set; }
    /// <summary>UTC instant the blob was first registered.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Set when the blob is queued for physical deletion (IX_BlobRef_Cleanup).</summary>
    public DateTime? PhysicallyDeletedAtUtc { get; set; }
}
