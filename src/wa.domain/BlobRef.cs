namespace wa.domain;

public class BlobRef
{
    public Guid Id { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int RefCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PhysicallyDeletedAtUtc { get; set; }
}
