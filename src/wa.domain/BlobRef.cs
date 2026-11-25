namespace wa.domain;

/// <summary>A ref-counted blob. TA-3.2: dbo.BlobRef.</summary>
public class BlobRef
{
    /// <summary>Gets or sets the ID of the blob ref.</summary>
    public Guid Id { get; set; }
    /// <summary>Blob path (unique).</summary>
    public string BlobPath { get; set; } = null!;
    /// <summary>Gets or sets the size bytes of the blob ref.</summary>
    public long SizeBytes { get; set; }
    /// <summary>Gets or sets the ref count of the blob ref.</summary>
    public int RefCount { get; set; }
    /// <summary>Gets or sets the created at UTC of the blob ref.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Set when the blob is queued for physical deletion (IX_BlobRef_Cleanup).</summary>
    public DateTime? PhysicallyDeletedAtUtc { get; set; }
}
