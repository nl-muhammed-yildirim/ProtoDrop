namespace wa.application.Ports;

/// <summary>
/// TA-3.5 — the blob-storage port (FR-055-1): ONE storage account, two containers
/// (<c>staging</c>, <c>transfers</c>). Adapters live in <c>wa.infrastructure</c>;
/// no Azure SDK type crosses this boundary. Paths are the canonical TA-3.5 rows
/// from <see cref="Blob.BlobPaths"/> — first segment is the container, the rest
/// the blob name (which may contain virtual directories).
/// </summary>
public interface IBlobStore
{
    /// <summary>Upload a blob at its canonical path, overwriting any existing blob there.</summary>
    /// <param name="content">The stream to upload. Not disposed by this method — the caller owns it.</param>
    Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Download a blob at its canonical path; returns the exact bytes (AC-055-1).</summary>
    /// <returns>A stream with the blob content. The caller must dispose it.</returns>
    Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default);
}
