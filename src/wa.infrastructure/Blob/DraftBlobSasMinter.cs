using wa.application.Ports;

namespace wa.infrastructure.Blob;

/// <summary>TA-3.6 — DI adapter over <see cref="BlobSasMinter"/>: the only production implementation of <see cref="IBlobSasMinter"/>.</summary>
public sealed class DraftBlobSasMinter : IBlobSasMinter, IExportSasMinter, IOrgLogoSasMinter
{
    private readonly BlobSasMinter _inner;

    /// <param name="accountUrl">The §5.2 blob account URL (Azurite locally, Azure Storage in dev/prod).</param>
    /// <param name="keyBase64">The §5.2 blob account key, base64.</param>
    public DraftBlobSasMinter(Uri accountUrl, string keyBase64) => _inner = new BlobSasMinter(accountUrl, keyBase64);

    /// <inheritdoc/>
    public Uri MintDraftUpload(string draftId, string fileId) => _inner.MintDraftUpload(draftId, fileId);

    /// <summary>T-003-06 (TA-4.2#6): 30-minute read SAS on one transfer file with the original-filename Content-Disposition override.</summary>
    public Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition) => _inner.MintRecipientFile(transferId, fileId, respContentDisposition);

    /// <summary>T-004-01: 30-minute read SAS on the whole-transfer zip (transfers container).</summary>
    public Uri MintRecipientZip(string transferId) => _inner.MintRecipientZip(transferId);

    /// <summary>US-047-01: 24 h read SAS on one data-export zip (exports container).</summary>
    public Uri MintExportDownload(Guid userId, Guid exportId) => _inner.MintExportDownload(userId, exportId);

    /// <summary>US-021-04: 30-minute inline read SAS on one org logo (avatars container).</summary>
    public Uri MintOrgLogo(Guid userId) => _inner.MintOrgLogo(userId);
}
