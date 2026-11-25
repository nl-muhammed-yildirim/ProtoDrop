using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage;
using Azure.Storage.Sas;
using wa.application.Blob;

namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.6 — mints storage-account–key SAS URLs for the browser, which uploads
/// and reads blobs directly against the storage account (FR-055-2). Minting is
/// pure HMAC-SHA256 over a string-to-sign — no network — so for a fixed clock
/// the result is deterministic and testable without a live account.
/// </summary>
/// <remarks>
/// TA-3.6 SAS matrix (all <c>blob</c>-resource scope, key-signed, absolute URL):
/// <list type="table">
/// <item>draft upload (per staged file) — permissions <c>cwr</c>, 2 h</item>
/// <item>recipient single file — permissions <c>r</c>, 30 min</item>
/// <item>recipient zip — permissions <c>r</c>, 30 min</item>
/// <item>data export zip (<c>exports</c> container) — permissions <c>r</c>, 24 h</item>
/// <item>org logo (<c>avatars</c> container) — permissions <c>r</c>, 30 min, inline</item>
/// </list>
/// The frozen <c>Azure.Storage.Blobs</c> 12.29.2 <see cref="BlobSasBuilder"/>
/// stamps its internal build date into the string-to-sign, so we take the SDK's
/// bit-exact query and string-to-sign via
/// <see cref="Azure.Storage.Sas.BlobSasBuilder.ToSasQueryParameters(Azure.Storage.StorageSharedKeyCredential, out string)"/>,
/// swap only the version line to <see cref="Version"/> (the spec literal, TA-3.6
/// "Version 2024"), re-sign with the account key, and emit the SDK's own query
/// with only <c>sv</c>/<c>sig</c> overridden — bit-exact, and the emitted
/// <c>sp</c> stays the literal <c>cwr</c>/<c>r</c>. The raw permission string is
/// preserved by <c>SetPermissions</c> (the SDK's own builder normalizes
/// it in <c>GenerateSasUri</c>, which we do not use).
/// </remarks>
public sealed class BlobSasMinter
{
    /// <summary>Storage service version written into the <c>sv</c> token of every SAS (TA-3.6 "Version 2024").</summary>
    internal const string Version = "2024";

    /// <summary>Validity of a draft-upload SAS: 2 hours (TA-3.6).</summary>
    internal static readonly TimeSpan DraftTtl = TimeSpan.FromHours(2);

    /// <summary>Validity of a recipient file, zip or logo SAS: 30 minutes (TA-3.6).</summary>
    internal static readonly TimeSpan RecipientTtl = TimeSpan.FromMinutes(30);

    /// <summary>Validity of a data-export download SAS: 24 hours (D-22 default).</summary>
    internal static readonly TimeSpan ExportTtl = TimeSpan.FromHours(24);

    private readonly Uri _accountUrl;
    private readonly string _keyBase64;
    private readonly StorageSharedKeyCredential _credential;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Initializes a new instance of the <see cref="BlobSasMinter"/> class.</summary>
    /// <param name="accountUrl">Storage account URL; only its authority is used in the minted URLs.</param>
    /// <param name="keyBase64">Storage account key, base64; used to sign the SAS tokens.</param>
    /// <param name="now">Optional time source (UTC) used for the SAS start and expiry; defaults to the system clock.</param>
    public BlobSasMinter(Uri accountUrl, string keyBase64,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(accountUrl);
        ArgumentNullException.ThrowIfNull(keyBase64);
        _accountUrl = accountUrl;
        _keyBase64 = keyBase64;
        _credential = new StorageSharedKeyCredential(
            AzureBlobStore.AccountNameFrom(accountUrl), keyBase64);
        _now = now ?? TimeProvider.System.GetUtcNow;
    }

    /// <summary>
    /// TA-3.6 draft upload (per staged file, <c>staging</c> container):
    /// permissions <c>cwr</c>, 2 h window, blob resource scope —
    /// Start = now, Expiry = now + 2 h (EC-055-1: a fresh mint after expiry works).
    /// </summary>
    /// <param name="draftId">Draft id, compact "N" form; part of the staging path.</param>
    /// <param name="fileId">File id, compact "N" form; part of the staging path.</param>
    /// <returns>An absolute SAS URL with <c>cwr</c> permissions that is valid for <see cref="DraftTtl"/>.</returns>
    public Uri MintDraftUpload(string draftId, string fileId)
        => Mint(
            BlobPaths.StagingFile(new Guid(draftId), new Guid(fileId)),
            permissions: "cwr",
            expiresOn: _now().Add(DraftTtl),
            startsOn: _now(),
            respContentDisposition: null);

    /// <summary>TA-3.6 recipient single file (<c>transfers</c> container): permissions <c>r</c>, 30 min, no start bound.</summary>
    /// <param name="transferId">Transfer id, compact "N" form.</param>
    /// <param name="fileId">File id, compact "N" form.</param>
    /// <returns>An absolute read-only SAS URL valid for <see cref="RecipientTtl"/>, without a Content-Disposition override.</returns>
    public Uri MintRecipientFile(string transferId, string fileId)
        => MintRecipientFile(transferId, fileId, respContentDisposition: null);

    /// <summary>
    /// T-003-06 (TA-4.2#6): mint a 30-minute read SAS on one transfer file with an original-filename Content-Disposition
    /// override (EC-003-3, RFC 5987 <c>filename*</c>). The override is part of the string-to-sign, so it must be provided at mint time.
    /// </summary>
    /// <param name="transferId">Transfer id, compact "N" form.</param>
    /// <param name="fileId">File id, compact "N" form.</param>
    /// <param name="respContentDisposition">Content-Disposition header the storage service returns on download; null or empty for none.</param>
    /// <returns>An absolute read-only SAS URL valid for <see cref="RecipientTtl"/>.</returns>
    public Uri MintRecipientFile(string transferId, string fileId, string? respContentDisposition)
        => Mint(
            BlobPaths.TransferFile(new Guid(transferId), new Guid(fileId)),
            permissions: "r",
            expiresOn: _now().Add(RecipientTtl),
            startsOn: null,
            respContentDisposition);

    /// <summary>Signed zip disposition (FR-003-10 iOS fallback, FR-017-3): explicit attachment named all.zip, RFC 5987 form.</summary>
    private const string ZipContentDisposition = "attachment;filename*=UTF-8''all.zip";

    /// <summary>
    /// TA-3.6 recipient zip (<c>transfers</c> container): permissions <c>r</c>, 30 min, no start bound.
    /// Signs an <c>attachment</c> Content-Disposition (all.zip) so mobile browsers treat it as a download (FR-003-10 / FR-017-3).
    /// </summary>
    /// <param name="transferId">Transfer id, compact "N" form.</param>
    /// <returns>An absolute read-only SAS URL for the transfer zip, valid for <see cref="RecipientTtl"/>.</returns>
    public Uri MintRecipientZip(string transferId)
        => Mint(
            BlobPaths.TransferZip(new Guid(transferId)),
            permissions: "r",
            expiresOn: _now().Add(RecipientTtl),
            startsOn: null,
            respContentDisposition: ZipContentDisposition);

    private const string ExportContentDisposition = "attachment;filename*=UTF-8''my-data.zip";

    /// <summary>US-047-01 (TA-3.6 export row): <c>exports</c> container, permissions <c>r</c>, 24 h, no start bound, attachment my-data.zip.</summary>
    /// <param name="userId">The user the export belongs to.</param>
    /// <param name="exportId">The export request id.</param>
    /// <returns>An absolute read-only SAS URL for the export zip, valid for <see cref="ExportTtl"/>.</returns>
    public Uri MintExportDownload(Guid userId, Guid exportId)
        => Mint(
            BlobPaths.DataExportZip(userId, exportId),
            permissions: "r",
            expiresOn: _now().Add(ExportTtl),
            startsOn: null,
            respContentDisposition: ExportContentDisposition);

    private const string LogoContentDisposition = "inline";

    /// <summary>US-021-04 (TA-3.6 org-logo row): <c>avatars</c> container, permissions <c>r</c>, 30 min, no start bound, inline Content-Disposition.</summary>
    /// <param name="userId">The user who owns the logo.</param>
    /// <returns>An absolute read-only SAS URL for the logo, valid for <see cref="RecipientTtl"/>, served inline.</returns>
    public Uri MintOrgLogo(Guid userId)
        => Mint(
            BlobPaths.OrgLogo(userId),
            permissions: "r",
            expiresOn: _now().Add(RecipientTtl),
            startsOn: null,
            respContentDisposition: LogoContentDisposition);

    private Uri Mint(
        string path, string permissions, DateTimeOffset expiresOn, DateTimeOffset? startsOn, string? respContentDisposition)
    {
        // TA-3.5 layout: the first path segment is the container; the rest is
        // the blob name (which itself may contain virtual directories).
        var cut = path.IndexOf('/');
        var (container, blobName) = (path[..cut], path[(cut + 1)..]);
        var builder = new BlobSasBuilder
        {
            Protocol = SasProtocol.Https,
            ExpiresOn = expiresOn,
            BlobContainerName = container,
            BlobName = blobName,
        };

        // T-003-06: the resp-content-disposition override is signed into the string-to-sign; must be provided at mint time.
        if (!string.IsNullOrEmpty(respContentDisposition))
            builder.ContentDisposition = respContentDisposition;

        // Raw (un-normalized) permission string: TA-3.6 literal "cwr"/"r".
        builder.SetPermissions(permissions);

        // Draft uploads are usable for 2 h: st=now, se=now+2 h. Recipient reads
        // carry no start bound (verified shape: no st token).
        if (startsOn.HasValue)
            builder.StartsOn = startsOn.Value;

        var query = builder.ToSasQueryParameters(
            _credential, out string stringToSign).ToString();
        query = Regex.Replace(query, "sv=[^&]*", "sv=" + Version);
        query = Regex.Replace(
            query,
            "sig=[^&]*",
            "sig=" + Uri.EscapeDataString(Resign(stringToSign)));

        // TA-3.6: URL form always https://{account}.blob.core.windows.net/…?sig=…
        return new Uri(_accountUrl.GetLeftPart(UriPartial.Authority)
            + "/" + container + "/" + blobName + "?" + query);
    }

    /// <summary>
    /// Re-signs the SDK's string-to-sign with only the version line swapped to
    /// <see cref="Version"/>: the line matching a bare <c>yyyy-MM-dd</c>. The
    /// signature is base64 per the Blob SAS spec.
    /// </summary>
    private string Resign(string stringToSign)
    {
        var lines = stringToSign.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (Regex.IsMatch(lines[i], @"^\d{4}-\d{2}-\d{2}$"))
            {
                lines[i] = Version;
                break;
            }

        using var hmac = new HMACSHA256(Convert.FromBase64String(_keyBase64));
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}
