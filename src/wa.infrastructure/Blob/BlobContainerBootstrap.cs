using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace wa.infrastructure.Blob;

/// <summary>
/// TA-3.5 - one-time-at-startup blob bootstrap. Creates the two
/// containers (<c>staging</c>, <c>transfers</c>) on ONE storage account,
/// then applies the lifecycle/retention bits the frozen
/// <c>Azure.Storage.Blobs</c> 12.29.2 SDK can express and logs the rest
/// ("apply what the backend supports; don't stall" — AGENT.md, T-007).
/// </summary>
/// <remarks>
/// Expressed in the frozen SDK:
/// <list type="bullet">
/// <item>container creation (<c>staging</c>, <c>transfers</c>) — both backends;</item>
/// <item><b>soft-delete 14 d ON</b> (<c>transfers</c> requirement, TA-3.5) as
/// the service-level <see cref="BlobServiceProperties.DeleteRetentionPolicy"/>
/// — the nearest expressible surface in 12.29.2.</item>
/// </list>
/// Logged (not applied) because 12.29.2 exposes no container-level
/// lifecycle API and no versioning toggle on the service properties:
/// <list type="bullet">
/// <item><c>staging/*</c> 24 h / <c>transfers/*</c> 30 d per-container
/// lifecycle rules (safety net only, F-TRF-005-8);</item>
/// <item><c>transfers</c> versioning OFF;</item>
/// <item>GRS (Geo-redundant) is a provisioning concern, prod-only.</item>
/// </list>
/// Each unsupported bit is a logged note (mirrored in PROGRESS.md §4) so a
/// later task can add a REST round-trip without changing the contract.
/// </remarks>
public sealed class BlobContainerBootstrap : BackgroundService
{
    private readonly BlobServiceClient _svc;
    private readonly ILogger<BlobContainerBootstrap> _logger;

    public BlobContainerBootstrap(
        BlobServiceClient serviceClient, ILogger<BlobContainerBootstrap> logger)
    {
        _svc = serviceClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure both TA-3.5 containers exist (idempotent).
        foreach (var name in new[] { "staging", "transfers" })
        {
            try
            {
                await _svc.GetBlobContainerClient(name)
                    .CreateIfNotExistsAsync(PublicAccessType.None, null, stoppingToken);
                _logger.LogInformation("Blob container '{C}' ensured", name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Don't stall the host: log and move on.
                _logger.LogWarning(ex, "Could not ensure container '{C}'", name);
            }
        }

        // Soft-delete 14 d ON (transfers requirement, TA-3.5) — the nearest
        // SDK-expressible surface in 12.29.2 is the service-level
        // retention policy.
        try
        {
            var props = await _svc.GetPropertiesAsync(stoppingToken);
            props.Value.DeleteRetentionPolicy = new BlobRetentionPolicy
            {
                Enabled = true,
                Days = 14,
            };
            await _svc.SetPropertiesAsync(props.Value, stoppingToken);
            _logger.LogInformation(
                "Blob service: soft-delete 14 d ON (TA-3.5 transfers)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not apply 14 d soft-delete (Azurite may not support service properties)");
        }

        // Not expressible in Azure.Storage.Blobs 12.29.2 (no container
        // lifecycle API, no versioning toggle): log, don't stall.
        _logger.LogInformation(
            "Blob lifecycle (TA-3.5, 12.29.2 not expressible): staging/* 24 h, " +
            "transfers/* 30 d, transfers versioning OFF, GRS (prod) — see PROGRESS.md §4");
    }
}
