using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.5 / TA-2.3 - the Azure Blobs adapter for <see cref="IBlobStore"/>
/// (frozen <c>Azure.Storage.Blobs</c>, TA-2.6). ONE storage account; the
/// <c>staging</c> and <c>transfers</c> containers live in it (TA-3.5).
/// Registered via <c>Wa:Blob:AccountUrl</c> + <c>Wa:Blob:AccountKey</c>
/// (AGENT.md §5.2) — Azurite locally, an Azure Storage account in prod.
/// </summary>
public sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _svc;

    /// <summary>
    /// Build directly from AGENT.md §5.2 config values. The credential
    /// needs the *account name*, not the host: an Azure URL carries it as
    /// the <c>{acct}.blob.{region}.core.windows.net</c> host prefix, an
    /// Azurite local-dev URL as the final path segment
    /// (e.g. <c>.../devstoreaccount1</c>).
    /// </summary>
    public AzureBlobStore(
        Uri accountUrl, string accountKey, ILogger<AzureBlobStore> logger)
    {
        var accountName = AccountNameFrom(accountUrl);
        logger.LogDebug("Blob: account {Account} from {Url}",
            accountName, accountUrl);
        _svc = new BlobServiceClient(accountUrl,
            new Azure.Storage.StorageSharedKeyCredential(accountName, accountKey));
    }

    /// <summary>
    /// Take an already-constructed <see cref="BlobServiceClient"/>
    /// (shared with <c>BlobContainerBootstrap</c>, test-friendly).
    /// </summary>
    public AzureBlobStore(BlobServiceClient serviceClient,
        ILogger<AzureBlobStore> logger)
    {
        _ = logger;
        _svc = serviceClient;
    }

    internal static string AccountNameFrom(Uri url)
    {
        var host = url.Host;
        if (host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal))
        {
            return host[..host.IndexOf('.', 1)];
        }
        // Azurite: http://127.0.0.1:10000/devstoreaccount1 — the last path
        // segment is the account name.
        var last = url.Segments[^1];
        return last == string.Empty ? host : last.TrimEnd('/');
    }

    public async Task<bool> ContainerExistsAsync(
        string container, CancellationToken cancellationToken = default)
    {
        var res = await _svc.GetBlobContainerClient(container)
            .ExistsAsync(cancellationToken);
        return res.Value;
    }

    public Task CreateContainerIfNotExistsAsync(
        string container, CancellationToken cancellationToken = default)
        => _svc.GetBlobContainerClient(container)
            .CreateIfNotExistsAsync(PublicAccessType.None, null, cancellationToken);

    public Task UploadAsync(
        string container,
        string name,
        Stream? content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return _svc.GetBlobContainerClient(container)
            .GetBlobClient(name)
            .UploadAsync(content, true, cancellationToken);
    }

    public async Task<Stream> DownloadAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default)
    {
        var download = await _svc.GetBlobContainerClient(container)
            .GetBlobClient(name)
            .DownloadAsync(cancellationToken);
        // Caller disposes; the port contract returns an owned stream.
        return download.Value.Content;
    }

    public async Task<bool> ExistsAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default)
    {
        var res = await _svc.GetBlobContainerClient(container)
            .GetBlobClient(name)
            .ExistsAsync(cancellationToken);
        return res.Value;
    }

    public Task DeleteAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default)
        => _svc.GetBlobContainerClient(container)
            .GetBlobClient(name)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.None, null,
                cancellationToken);
}