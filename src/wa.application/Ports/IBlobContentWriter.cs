namespace wa.application.Ports;

/// <summary>US-021-04: upload with an explicit Content-Type (org logo — served inline, so the stored type matters). Overwrite semantics like <see cref="IBlobStore.UploadAsync"/>.</summary>
public interface IBlobContentWriter
{
    /// <summary>Uploads a blob with an explicit Content-Type, overwriting any existing blob at the path.</summary>
    /// <param name="content">The stream to upload. Not disposed by this method — the caller owns it.</param>
    /// <param name="path">Canonical blob path, container first (see <see cref="Blob.BlobPaths"/>).</param>
    /// <param name="contentType">MIME type stored with the blob and returned when it is served.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
}
