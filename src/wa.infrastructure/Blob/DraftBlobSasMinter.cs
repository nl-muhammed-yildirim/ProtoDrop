using wa.application.Ports;

namespace wa.infrastructure.Blob;

/// <summary>TA-3.6 — DI adapter over <see cref="BlobSasMinter"/>: the only production implementation of <see cref="IBlobSasMinter"/>.</summary>
public sealed class DraftBlobSasMinter : IBlobSasMinter
{
    private readonly BlobSasMinter _inner;

    /// <param name="accountUrl">The §5.2 blob account URL (Azurite locally, Azure Storage in dev/prod).</param>
    /// <param name="keyBase64">The §5.2 blob account key, base64.</param>
    public DraftBlobSasMinter(Uri accountUrl, string keyBase64) => _inner = new BlobSasMinter(accountUrl, keyBase64);

    /// <inheritdoc/>
    public Uri MintDraftUpload(string draftId, string fileId) => _inner.MintDraftUpload(draftId, fileId);
}
