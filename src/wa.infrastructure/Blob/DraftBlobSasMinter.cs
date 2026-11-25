using wa.application.Ports;

namespace wa.infrastructure.Blob;

/// <summary>TA-3.6 — DI adapter over <see cref="BlobSasMinter"/>: the only production implementation of <see cref="IBlobSasMinter"/>.</summary>
public sealed class DraftBlobSasMinter : IBlobSasMinter, IExportSasMinter, IOrgLogoSasMinter
{
    private readonly BlobSasMinter _inner;

    /// <summary>Initializes a new instance of the <see cref="DraftBlobSasMinter"/> class.</summary>
    /// <param name="accountUrl">The §5.2 blob account URL (Azurite locally, Azure Storage in dev/prod).</param>
    /// <param name="keyBase64">The §5.2 blob account key, base64.</param>
    public DraftBlobSasMinter(Uri accountUrl, string keyBase64) => _inner = new BlobSasMinter(accountUrl, keyBase64);

    /// <inheritdoc/>
    public Uri MintDraftUpload(string draftId, string fileId) => _inner.MintDraftUpload(draftId, fileId);

    /// <summary>T-003-06 (TA-4.2#6): 30-minute read SAS on one transfer file with the original-filename Content-Disposition override.</summary>
    /// <param name="transferId">Transfer id, compact "N" form.</param>
    /// <param name="fileId">File id, compact "N" form.</param>
    /// <param name="respContentDisposition">Content-Disposition header the storage service returns on download.</param>
    /// <returns>An absolute read-only SAS URL valid for 30 minutes.</returns>
    public Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition) => _inner.MintRecipientFile(transferId, fileId, respContentDisposition);

    /// <summary>T-004-01: 30-minute read SAS on the whole-transfer zip (transfers container).</summary>
    /// <param name="transferId">Transfer id, compact "N" form.</param>
    /// <returns>An absolute read-only SAS URL for the transfer zip, valid for 30 minutes.</returns>
    public Uri MintRecipientZip(string transferId) => _inner.MintRecipientZip(transferId);

    /// <summary>US-047-01: 24 h read SAS on one data-export zip (exports container).</summary>
    /// <param name="userId">The user the export belongs to.</param>
    /// <param name="exportId">The export request id.</param>
    /// <returns>An absolute read-only SAS URL for the export zip, valid for 24 hours.</returns>
    public Uri MintExportDownload(Guid userId, Guid exportId) => _inner.MintExportDownload(userId, exportId);

    /// <summary>US-021-04: 30-minute inline read SAS on one org logo (avatars container).</summary>
    /// <param name="userId">The user who owns the logo.</param>
    /// <returns>An absolute read-only inline SAS URL for the logo, valid for 30 minutes.</returns>
    public Uri MintOrgLogo(Guid userId) => _inner.MintOrgLogo(userId);
}
