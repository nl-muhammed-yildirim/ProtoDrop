using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.6 — mints storage-account–key ("Version 2024") SAS URLs for the
/// browser, which uploads/reads blobs directly against the storage account.
/// Minting is a pure HMAC-SHA256 over the string-to-sign — no network — so
/// for a fixed <paramref name="now"/> the result is deterministic and
/// testable without a live account.
/// </summary>
/// <remarks>
/// TA-3.6 SAS matrix (all <c>blob</c>-resource scope, key-signed, absolute
/// <c>https://{account}.blob.core.windows.net</c> form):
/// <list type="table">
/// <item>draft upload (per staged file) — permissions <c>cwr</c>, 2 h</item>
/// <item>recipient single file — permissions <c>r</c>, 30 min</item>
/// <item>recipient zip — permissions <c>r</c>, 30 min</item>
/// </list>
/// The frozen <c>Azure.Storage.Blobs</c> 12.29.2 <see cref="BlobSasBuilder"/>
/// stamps its internal build date into the string-to-sign (and
/// <c>GenerateSasUri</c> re-normalizes the permission string), but
/// <see cref="BlobSasBuilder.ToSasQueryParameters(StorageSharedKeyCredential, out string)"/>
/// preserves the raw permission string and the version line verbatim. We
/// take that string-to-sign, swap the version line to
/// <see cref="Version"/> (TA-3.6 "Version 2024"), re-sign, and emit the
/// SDK's own query with only <c>sv</c>/<c>sig</c> overridden — bit-exact,
/// and the emitted <c>sp</c> stays the literal <c>cwr</c>/<c>r</c>.
/// </remarks>
public sealed class BlobSasMinter
{
    /// <summary>
    /// TA-3.6: "the server issues … Version 2024 SAS." 2024-08-04 is the
    /// latest public 2024-era blob API release used for the sv token.
    /// </summary>
    internal const string Version = "2024-08-04";

    /// <summary>TA-3.6 draft upload TTL: 2 h.</summary>
    internal static readonly TimeSpan DraftTtl = TimeSpan.FromHours(2);

    /// <summary>TA-3.6 recipient single-file/zip TTL: 30 min.</summary>
    internal static readonly TimeSpan RecipientTtl = TimeSpan.FromMinutes(30);

    private readonly Uri _accountUrl;
    private readonly string _keyBase64;
    private readonly StorageSharedKeyCredential _credential;

    public BlobSasMinter(Uri accountUrl, string accountKey)
    {
        _accountUrl = accountUrl ?? throw new ArgumentNullException(nameof(accountUrl));
        _keyBase64 = accountKey ?? throw new ArgumentNullException(nameof(accountKey));
        _credential = new StorageSharedKeyCredential(
            AzureBlobStore.AccountNameFrom(accountUrl), _keyBase64);
    }

    /// <summary>
    /// TA-3.6 draft upload (per staged file, <c>staging</c> container):
    /// permissions <c>cwr</c>, TTL 2 h, <c>blob</c> resource scope.
    /// Start = now, expiry = now + 2 h (the "draft is usable for 2 h" window).
    /// </summary>
    public Uri MintDraftUpload(string draftId, string fileId, DateTimeOffset now)
        => Mint(BlobPaths.Staging(draftId, fileId),
            "cwr", now.Add(DraftTtl),
            allowStart: true, startsOn: now);

    /// <summary>
    /// TA-3.6 recipient single file (<c>transfers</c> container):
    /// permissions <c>r</c>, TTL 30 min, <c>blob</c> resource scope.
    /// </summary>
    public Uri MintRecipientFile(
        string transferId, string fileId, DateTimeOffset now)
        => Mint(
            BlobPaths.TransferFile(transferId, fileId),
            permissions: "r",
            expiresOn: now.Add(RecipientTtl),
            allowStart: false, startsOn: null);

    /// <summary>
    /// TA-3.6 recipient zip (<c>transfers</c> container):
    /// permissions <c>r</c>, TTL 30 min, <c>blob</c> resource scope.
    /// </summary>
    public Uri MintRecipientZip(string transferId, DateTimeOffset now)
        => Mint(
            BlobPaths.TransferZip(transferId),
            permissions: "r",
            expiresOn: now.Add(RecipientTtl),
            allowStart: false, startsOn: null);

    private Uri Mint(
        string path, string permissions, DateTimeOffset expiresOn,
        bool allowStart, DateTimeOffset? startsOn)
    {
        // TA-3.5 layout: the first path segment is the container; the rest
        // is the blob name (which itself may contain virtual directories).
        var cut = path.IndexOf('/');
        var (container, blob) = cut < 0
            ? (path, string.Empty)
            : (path[..cut], path[(cut + 1)..]);
        var builder = new BlobSasBuilder
        {
            Protocol = SasProtocol.Https,
            ExpiresOn = expiresOn,
            BlobName = blob,
            BlobContainerName = container,
        };

        // Raw (un-normalized) permission string: TA-3.6 literal "cwr"/"r".
        builder.SetPermissions(permissions);

        // Draft uploads are usable for 2 h: sign with st=now, se=now+2 h.
        // Recipient reads have no start bound (verified shape: no st token).
        if (allowStart && startsOn.HasValue)
            builder.StartsOn = startsOn.Value;

        // The SDK's bit-exact query; only sv/sig are overridden below.
        var query = builder.ToSasQueryParameters(
            _credential, out string stringToSign).ToString();
        query = Regex.Replace(query, @"sv=[^&]*", "sv=" + Version);
        query = Regex.Replace(
            query,
            @"sig=[^&]*",
            "sig=" + Uri.EscapeDataString(Resign(stringToSign)));

        // "SAS URLs are always of the form
        // https://{account}.blob.core.windows.net/<container>/<blob>?sig=…"
        return new Uri(_accountUrl.GetLeftPart(UriPartial.Authority)
            + "/" + container + "/" + blob + "?" + query);
    }

    /// <summary>
    /// Re-signs the string-to-sign with the TA-3.6 version line swapped in.
    /// The frozen SDK stamps <c>sv</c> with an internal build date
    /// (2026-06-06 in 12.29.2), so "Version 2024" is honored by swapping
    /// the version line (the only line that is a bare <c>yyyy-MM-dd</c>)
    /// and recomputing the HMAC-SHA256 signature with the account key.
    /// </summary>
    private string Resign(string stringToSign)
    {
        var lines = stringToSign.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i] is not null && Regex.IsMatch(lines[i], @"^\d{4}-\d{2}-\d{2}$"))
            {
                lines[i] = Version;
                break;
            }

        using var hmac =
            new HMACSHA256(Convert.FromBase64String(_keyBase64));
        return Convert.ToBase64String(
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }
}
