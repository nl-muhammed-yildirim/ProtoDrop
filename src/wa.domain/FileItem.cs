namespace wa.domain;

public class FileItem
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid BlobRefId { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int SortOrder { get; set; }
}