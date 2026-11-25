namespace wa.application.Ports;

/// <summary>US-021-04: upload with an explicit Content-Type (org logo — served inline, so the stored type matters). Overwrite semantics like <see cref="IBlobStore.UploadAsync"/>.</summary>
public interface IBlobContentWriter
{
    /// <summary>Uploads the operation.</summary>
    /// <param name="content">The stream to upload. Not disposed by this method — the caller owns it.</param>
    /// <param name="path">The path.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
}
