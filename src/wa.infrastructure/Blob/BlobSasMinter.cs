using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage;
using Azure.Storage.Blobs;
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
/// </list>
/// The frozen <c>Azure.Storage.Blobs</c> 12.29.2 <see cref="BlobSasBuilder"/>
/// stamps its internal build date into the string-to-sign, so we take the SDK's
/// bit-exact query and string-to-sign via
/// <see cref="Azure.Storage.Sas.BlobSasBuilder.ToSasQueryParameters(Azure.Storage.StorageSharedKeyCredential, out string)"/>,
/// swap only the version line to <see cref="Version"/> (the spec literal, TA-3.6
/// "Version 2024"), re-sign with the account key, and emit the SDK's own query
/// with only <c>sv</c>/<c>sig</c> overridden — bit-exact, and the emitted
/// <c>sp</c> stays the literal <c>cwr</c>/<c>r</c>. The raw permission string is
/// preserved by <see cref="SetPermissions"/> (the SDK's own builder normalizes
/// it in <c>GenerateSasUri</c>, which we do not use).
/// </remarks>
public sealed class BlobSasMinter
{
    /// <summary>TA-3.6 "Version 2024" — the spec literal for the sv token.</summary>
    internal const string Version = "2024";

    /// <summary>TA-3.6 draft upload TTL: 2 h (constant, not a local setting).</summary>
    internal static readonly TimeSpan DraftTtl = TimeSpan.FromHours(2);

    /// <summary>TA-3.6 recipient single-file/zip TTL: 30 min (constant, not a local setting).</summary>
    internal static readonly TimeSpan RecipientTtl = TimeSpan.FromMinutes(30);

    private readonly Uri _accountUrl;
    private readonly string _keyBase64;
    private readonly StorageSharedKeyCredential _credential;
    private readonly Func<DateTimeOffset> _now;

    public BlobSasMinter(Uri accountUrl, string keyBase64,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(accountUrl);
        ArgumentNullException.ThrowIfNull(keyBase64);
        _accountUrl = accountUrl;
        _keyBase64 = keyBase64;
        _credential = new StorageSharedKeyCredential(
            AzureBlobStore.AccountNameFrom(accountUrl), keyBase64);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// TA-3.6 draft upload (per staged file, <c>staging</c> container):
    /// permissions <c>cwr</c>, 2 h window, blob resource scope —
    /// Start = now, Expiry = now + 2 h (EC-055-1: a fresh mint after expiry works).
    /// </summary>
    public Uri MintDraftUpload(string draftId, string fileId)
        => Mint(
            BlobPaths.StagingFile(new Guid(draftId), new Guid(fileId)),
            permissions: "cwr",
            expiresOn: _now().Add(DraftTtl),
            startsOn: _now(),
            respContentDisposition: null);

    /// <summary>TA-3.6 recipient single file (<c>transfers</c> container): permissions <c>r</c>, 30 min, no start bound.</summary>
    public Uri MintRecipientFile(string transferId, string fileId)
        => MintRecipientFile(transferId, fileId, respContentDisposition: null);

    /// <summary>
    /// T-003-06 (TA-4.2#6): mint a 30-minute read SAS on one transfer file with an original-filename Content-Disposition
    /// override (EC-003-3, RFC 5987 <c>filename*</c>). The override is part of the string-to-sign, so it must be provided at mint time.
    /// </summary>
    public Uri MintRecipientFile(string transferId, string fileId, string? respContentDisposition)
        => Mint(
            BlobPaths.TransferFile(new Guid(transferId), new Guid(fileId)),
            permissions: "r",
            expiresOn: _now().Add(RecipientTtl),
            startsOn: null,
            respContentDisposition);

    /// <summary>TA-3.6 recipient zip (<c>transfers</c> container): permissions <c>r</c>, 30 min, no start bound.</summary>
    public Uri MintRecipientZip(string transferId)
        => Mint(
            BlobPaths.TransferZip(new Guid(transferId)),
            permissions: "r",
            expiresOn: _now().Add(RecipientTtl),
            startsOn: null,
            respContentDisposition: null);

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

/// <summary>TA-9.6 — mask the sig= value in any log output (SAS never appears unmasked).</summary>
internal static class SasLogMask
{
    public static string MaskSig(this Uri url) => Regex.Replace(
        url.ToString(), "sig=[^&]*", "sig=***");
}
