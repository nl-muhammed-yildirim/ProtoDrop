using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Ports;

namespace wa.application.UseCases.DataExport;

/// <summary>
/// US-047-01 (FR-047-1/2/8, TA-6.1/6.2): f-data-export timer handler. Claims a 5-min JobRun window, purges ready exports
/// past retention, then builds every processing export into exports/{userId}/{exportId}.zip. No events, no telemetry (FR-047-8).
/// The feature flag is deliberately not checked: the gate stops creation, not completion (FR-047-4).
/// </summary>
public sealed class BuildDataExportsHandler
{
    private readonly IDataExportJobStore _store;
    private readonly IBlobStore _blobs;
    private readonly ILogger<BuildDataExportsHandler> _logger;

    public BuildDataExportsHandler(IDataExportJobStore store, IBlobStore blobs, ILogger<BuildDataExportsHandler> logger)
    {
        _store = store;
        _blobs = blobs;
        _logger = logger;
    }

    /// <summary>Runs one pass. Returns the number of exports built to ready (0 when the claim was lost).</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var windowStart = TruncateTo5Min(nowUtc);
        if (!await _store.TryClaimAsync(windowStart, nowUtc.AddMinutes(10), ct))
            return 0;

        try
        {
            await PurgeAsync(nowUtc, ct);

            var built = 0;
            foreach (var row in await _store.GetProcessingAsync(DataExportPolicy.BatchSize, ct))
            {
                try
                {
                    var snapshot = await _store.ReadSnapshotAsync(row.AppUserId, ct);
                    if (snapshot is null)
                    {
                        // EC-047-3: user deleted mid-flight — fail cleanly.
                        _logger.LogInformation("f-data-export: user gone, failing export {ExportId}", row.Id);
                        await _store.MarkFailedAsync(row.Id, ct);
                        continue;
                    }

                    var path = BlobPaths.DataExportZip(row.AppUserId, row.Id);
                    await using (var content = new MemoryStream(DataExportArchive.Build(snapshot)))
                        await _blobs.UploadAsync(path, content, ct);
                    await _store.MarkReadyAsync(row.Id, path, nowUtc, ct);
                    built++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // EC-047-2: the row fails, the user can retry; the pass continues.
                    _logger.LogError(ex, "f-data-export failed for export {ExportId}", row.Id);
                    await _store.MarkFailedAsync(row.Id, ct);
                }
            }
            return built;
        }
        finally
        {
            await _store.CompleteClaimAsync(windowStart, ct);
        }
    }

    private async Task PurgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        foreach (var item in await _store.GetPurgeableAsync(nowUtc - DataExportPolicy.StoredRetention, DataExportPolicy.BatchSize, ct))
        {
            try
            {
                await _blobs.DeleteAsync(item.BlobPath, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "f-data-export: blob delete failed for export {ExportId}", item.Id);
            }
            await _store.DeleteAsync(item.Id, ct);
        }
    }

    /// <summary>Truncate a UTC DateTime to its 5-minute boundary (TA-6.1 JobRun window key).</summary>
    public static DateTime TruncateTo5Min(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 5) * 5, 0, DateTimeKind.Utc);
}
