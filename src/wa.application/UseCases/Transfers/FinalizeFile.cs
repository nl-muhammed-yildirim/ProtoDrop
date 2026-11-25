namespace wa.application.UseCases.Transfers;

/// <summary>One file in the finalize request (TA-4.2#2 request row). POCO with settable properties for minimal-API binding.</summary>
public sealed class FinalizeFile
{
    /// <summary>File id issued by the create-draft call; names the staged blob.</summary>
    public Guid FileId { get; set; }
    /// <summary>Original file name.</summary>
    public string Name { get; set; } = null!;
    /// <summary>Size in bytes the client declares; checked against the staged blob.</summary>
    public long SizeBytes { get; set; }
    /// <summary>MIME type reported by the browser, or null when unknown.</summary>
    public string? ContentType { get; set; }
    /// <summary>Number of upload retries the client needed for this file; summed into the <c>upload_completed</c> telemetry.</summary>
    public int Retries { get; set; }
}
