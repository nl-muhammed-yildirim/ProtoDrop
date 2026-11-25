using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.5 / TA-2.6 — the Azure Blobs adapter for <see cref="IBlobStore"/>
/// (frozen <c>Azure.Storage.Blobs</c>). ONE storage account, two containers
/// (<c>staging</c>, <c>transfers</c>) per TA-3.5; locally Azurite via the §5.2
/// env vars, an Azure Storage account in dev/prod (TA-0.2 rule 7: no account
/// key in browser-facing code or logs). Paths are canonical TA-3.5 rows from
/// <see cref="wa.application.Blob.BlobPaths"/> — the first segment is the
/// container, the rest the blob name.
/// </summary>
public sealed partial class AzureBlobStore : IBlobStore, IBlobContentWriter
{
    private readonly BlobServiceClient _svc;

    /// <summary>
    /// Build directly from AGENT.md §5.2 config values. The credential needs
    /// the *account name*, not the host: an Azure URL carries it as the
    /// <c>{acct}.blob.core.windows.net</c> host prefix, an Azurite local-dev
    /// URL as the final path segment (e.g. <c>…/devstoreaccount1</c>).
    /// </summary>
    /// <param name="accountUrl">Blob service URL: an Azure host or an Azurite URL that ends with the account name.</param>
    /// <param name="accountKey">Storage account key (base64) used for the shared-key credential.</param>
    /// <param name="logger">Logger used to report the resolved account name at debug level.</param>
    public AzureBlobStore(
        Uri accountUrl, string accountKey, ILogger<AzureBlobStore> logger)
    {
        var accountName = AccountNameFrom(accountUrl);
        LogBlobAccount(logger, accountName, accountUrl);
        _svc = new BlobServiceClient(
            accountUrl,
            new Azure.Storage.StorageSharedKeyCredential(accountName, accountKey));
    }

    /// <summary>Take an already-constructed client (integration tests build one against Testcontainers Azurite).</summary>
    /// <param name="serviceClient">Ready-made client, for example one pointing at a Testcontainers Azurite instance.</param>
    public AzureBlobStore(BlobServiceClient serviceClient) => _svc = serviceClient;

    /// <summary>Derives the storage account name that shared-key credentials need from the account URL (TA-0.2 rule 7).</summary>
    /// <param name="url">Blob service URL; an Azure host carries the name as its first label, an Azurite URL as its last path segment.</param>
    /// <returns>The account name, or the host itself when the URL carries no path segment.</returns>
    public static string AccountNameFrom(Uri url)
    {
        var host = url.Host;
        if (host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal))
            return host[..host.IndexOf('.', 1)];
        // Azurite: http://127.0.0.1:10000/devstoreaccount1 — the last path segment is the account name; a bare test host yields the host itself.
        var last = url.Segments[^1];
        var name = last.TrimEnd('/');
        return name.Length == 0 ? host : name;
    }
    /// <inheritdoc />
    public Task UploadAsync(
        string path, Stream content, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        return _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .UploadAsync(content, true, cancellationToken);
    }

    /// <summary>Uploads a blob with an explicit Content-Type, overwriting any existing blob at the path (US-021-04).</summary>
    /// <param name="path">Canonical blob path, container first.</param>
    /// <param name="content">The stream to upload; not disposed here.</param>
    /// <param name="contentType">MIME type stored with the blob.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    public Task UploadAsync(
        string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        return _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .UploadAsync(
                content,
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        var download = await _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .DownloadAsync(cancellationToken);
        // The port contract returns an owned stream; the caller disposes it.
        return download.Value.Content;
    }

    /// <inheritdoc />
    public async Task CopyAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var (srcContainer, srcBlobName) = SplitPath(sourcePath);
        var (dstContainer, dstBlobName) = SplitPath(destinationPath);
        // Server-side copy — no bytes pass through this process. SyncCopyFromUri is synchronous in the SDK:
        // it returns only after the destination holds exactly the source blob's bytes, which finalize relies on
        // before writing DB rows that reference the transfer path (TA-4.2#2 step 4 ordering). StartCopyFromUri
        // would instead return a Pending operation and let finalize race ahead of the copy finishing.
        var srcUrl = _svc.GetBlobContainerClient(srcContainer)
            .GetBlobClient(srcBlobName)
            .Uri;
        await _svc.GetBlobContainerClient(dstContainer)
            .GetBlobClient(dstBlobName)
            .SyncCopyFromUriAsync(srcUrl, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetSizeAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        // Metadata read only — no content download; a 2.5 GB blob must not be streamed into RAM for the ±0 check.
        var info = await _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .GetPropertiesAsync(cancellationToken: cancellationToken);
        return info.Value.ContentLength;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        var exists = await _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .ExistsAsync(cancellationToken);
        return exists.Value;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var (container, blobName) = SplitPath(path);
        return _svc.GetBlobContainerClient(container)
            .GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private static (string container, string blobName) SplitPath(string path)
    {
        var cut = path.IndexOf('/');
        if (cut <= 0 || cut == path.Length - 1)
            throw new ArgumentException(
                "Path must be a canonical TA-3.5 row '<container>/<blob name>': " + path,
                nameof(path));
        return (path[..cut], path[(cut + 1)..]);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Blob: account {Account} from {Url}")]
    private static partial void LogBlobAccount(ILogger logger, string account, Uri url);
}
