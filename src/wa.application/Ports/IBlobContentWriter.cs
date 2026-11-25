namespace wa.application.Ports;

/// <summary>US-021-04: upload with an explicit Content-Type (org logo — served inline, so the stored type matters). Overwrite semantics like <see cref="IBlobStore.UploadAsync"/>.</summary>
public interface IBlobContentWriter
{
    /// <param name="content">The stream to upload. Not disposed by this method — the caller owns it.</param>
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);
}
