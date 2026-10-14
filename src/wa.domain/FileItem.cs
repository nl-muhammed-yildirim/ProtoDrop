namespace wa.domain;

/// <summary>A file belonging to a transfer. TA-3.2: dbo.FileItem.</summary>
public class FileItem
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid BlobRefId { get; set; }
    public string OriginalName { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int SortOrder { get; set; }
}
