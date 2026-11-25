namespace wa.domain;

/// <summary>A file belonging to a transfer. TA-3.2: dbo.FileItem.</summary>
public class FileItem
{
    /// <summary>Gets or sets the ID of the file item.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the transfer ID of the file item.</summary>
    public Guid TransferId { get; set; }
    /// <summary>Gets or sets the blob ref ID of the file item.</summary>
    public Guid BlobRefId { get; set; }
    /// <summary>Gets or sets the original name of the file item.</summary>
    public string OriginalName { get; set; } = null!;
    /// <summary>Gets or sets the size bytes of the file item.</summary>
    public long SizeBytes { get; set; }
    /// <summary>Gets or sets the content type of the file item.</summary>
    public string? ContentType { get; set; }
    /// <summary>Gets or sets the sort order of the file item.</summary>
    public int SortOrder { get; set; }
}
