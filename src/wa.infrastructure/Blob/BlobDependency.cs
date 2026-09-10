using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace wa.infrastructure.Blob;

/// <summary>
/// T-007a - blob registration (AGENT.md §5.2, TA-3.5/TA-2.3).
/// Registers <see cref="IBlobStore"/> (the Azure adapter) and
/// <see cref="BlobContainerBootstrap"/> — only when
/// <c>Wa:Blob:AccountUrl</c> and <c>Wa:Blob:AccountKey</c> are both set
/// (Azurite locally / Azure in prod). When either is unset, nothing is
/// registered (tests and environments without a blob backend can hold
/// a fake, the AGENT.md §5.2 pattern).
/// No new NuGet packages (TA-2.3 / TA-2.6: frozen <c>Azure.Storage.Blobs</c>).
/// </summary>
public static class BlobDependency
{
    /// <summary>
    /// Register the Azure Blobs adapter and container bootstrap.
    /// </summary>
    public static IServiceCollection AddBlobStore(
        this IServiceCollection services, IConfiguration configuration)
    {
        var accountUrl = configuration["Wa:Blob:AccountUrl"];
        var accountKey = configuration["Wa:Blob:AccountKey"];

        if (string.IsNullOrWhiteSpace(accountUrl) ||
            string.IsNullOrWhiteSpace(accountKey))
        {
            // No blob backend configured: no registration. Callers that
            // need an <see cref="IBlobStore"/> must provide a fake (tests
            // / environments without a storage backend).
            return services;
        }

        services.AddSingleton<BlobServiceClient>(sp =>
            new BlobServiceClient(
                new Uri(accountUrl),
                new Azure.Storage.StorageSharedKeyCredential(
                    AzureBlobStore.AccountNameFrom(new Uri(accountUrl)), accountKey!)));

        services.AddSingleton<IBlobStore>(sp =>
            new AzureBlobStore(sp.GetRequiredService<BlobServiceClient>(),
                sp.GetRequiredService<ILogger<AzureBlobStore>>()));

        // TA-3.6: shared-key ("Version 2024") SAS for browser
        // uploads/downloads, signed from the same account key in config.
        services.AddSingleton<BlobSasMinter>(sp =>
            new BlobSasMinter(new Uri(accountUrl), accountKey!));

        // One-time-at-startup: create staging/transfers + apply the
        // SDK-expressible lifecycle bits (TA-3.5).
        services.AddHostedService<BlobContainerBootstrap>();

        return services;
    }
}