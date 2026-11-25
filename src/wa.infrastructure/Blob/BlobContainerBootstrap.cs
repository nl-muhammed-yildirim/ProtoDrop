using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using wa.application.Blob;

namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.5 — one-time-at-startup blob bootstrap: creates the containers
/// (<c>staging</c>, <c>transfers</c>, <c>exports</c>, <c>avatars</c> — the last has no lifecycle rule, deleted with the account) on ONE storage account, then applies the
/// lifecycle/retention bits the frozen <c>Azure.Storage.Blobs</c> 12.29.2 SDK
/// can express and logs the rest ("apply what the backend supports; don't
/// stall" — AGENT.md, T-055-02 "create them if missing in local setup").
/// </summary>
/// <remarks>
/// Expressed in the frozen SDK:
/// <list type="bullet">
/// <item>container creation (<c>staging</c>, <c>transfers</c>) — both backends;</item>
/// <item><b>soft-delete 14 d ON</b> (TA-3.5 <c>transfers</c> requirement) as the service-level
/// <see cref="BlobServiceProperties.DeleteRetentionPolicy"/> — the nearest expressible surface in 12.29.2.</item>
/// </list>
/// Logged (not applied) because 12.29.2 exposes no container-level lifecycle API and no
/// versioning toggle on the service properties:
/// <list type="bullet">
/// <item><c>staging/*</c> 24 h / <c>transfers/*</c> 30 d per-container lifecycle rules (safety net only, F-TRF-005-8);</item>
/// <item><c>transfers</c> versioning OFF;</item>
/// <item>GRS (Geo-redundant) is a provisioning concern, prod-only.</item>
/// </list>
/// Each unsupported bit is a logged note so a later task can add a REST round-trip without changing the contract.
/// </remarks>
public sealed partial class BlobContainerBootstrap : BackgroundService
{
    private readonly BlobServiceClient _svc;
    private readonly ILogger<BlobContainerBootstrap> _logger;

    /// <summary>Initializes a new instance of the <see cref="BlobContainerBootstrap"/> class.</summary>
    /// <param name="serviceClient">Blob service client used to create the containers.</param>
    /// <param name="logger">Logger for the bootstrap progress and the settings that cannot be applied.</param>
    public BlobContainerBootstrap(
        BlobServiceClient serviceClient, ILogger<BlobContainerBootstrap> logger)
    {
        _svc = serviceClient;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure both TA-3.5 containers exist (idempotent).
        foreach (var name in new[] { BlobPaths.StagingContainer, BlobPaths.TransfersContainer, BlobPaths.ExportsContainer, BlobPaths.AvatarsContainer })
        {
            try
            {
                await _svc.GetBlobContainerClient(name)
                    .CreateIfNotExistsAsync(PublicAccessType.None, null, stoppingToken);
                LogContainerEnsured(_logger, name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Don't stall the host: log and move on.
                LogContainerEnsureFailed(_logger, ex, name);
            }
        }

        // Soft-delete 14 d ON (TA-3.5 transfers requirement) — the nearest SDK-expressible surface in 12.29.2 is the service-level retention policy.
        try
        {
            var props = await _svc.GetPropertiesAsync(stoppingToken);
            props.Value.DeleteRetentionPolicy = new BlobRetentionPolicy
            {
                Enabled = true,
                Days = 14,
            };
            await _svc.SetPropertiesAsync(props.Value, stoppingToken);
            LogSoftDeleteEnabled(_logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSoftDeleteNotApplied(_logger, ex);
        }

        // Not expressible in Azure.Storage.Blobs 12.29.2 (no container lifecycle API, no versioning toggle): log, don't stall.
        LogLifecycleNotes(_logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Blob container '{C}' ensured")]
    private static partial void LogContainerEnsured(ILogger logger, string c);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ensure container '{C}'")]
    private static partial void LogContainerEnsureFailed(ILogger logger, Exception exception, string c);

    [LoggerMessage(Level = LogLevel.Information, Message = "Blob service: soft-delete 14 d ON (TA-3.5 transfers)")]
    private static partial void LogSoftDeleteEnabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not apply 14 d soft-delete (Azurite may not support service properties)")]
    private static partial void LogSoftDeleteNotApplied(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Blob lifecycle (TA-3.5, 12.29.2 not expressible): staging/* 24 h, transfers/* 30 d, exports/* 7 d (f-data-export purge is primary), avatars/* no lifecycle (deleted with the account), transfers versioning OFF, GRS (prod)")]
    private static partial void LogLifecycleNotes(ILogger logger);
}
