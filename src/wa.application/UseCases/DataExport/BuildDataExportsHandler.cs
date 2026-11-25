using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Ports;

namespace wa.application.UseCases.DataExport;

/// <summary>
/// US-047-01 (FR-047-1/2/8, TA-6.1/6.2): f-data-export timer handler. Claims a 5-min JobRun window, purges ready exports
/// past retention (ready zips of deleted accounts are purged on the next pass), then builds every processing export into exports/{userId}/{exportId}.zip. No events, no telemetry (FR-047-8).
/// The feature flag is deliberately not checked: the gate stops creation, not completion (FR-047-4).
/// </summary>
/// <param name="store">Job store that claims the window and reads and updates export rows.</param>
/// <param name="blobs">Blob store that receives the built zips and deletes purged ones.</param>
/// <param name="logger">Logger for diagnostics (never logs user data).</param>
public sealed partial class BuildDataExportsHandler(IDataExportJobStore store, IBlobStore blobs, ILogger<BuildDataExportsHandler> logger)
{
    private readonly IDataExportJobStore _store = store;
    private readonly IBlobStore _blobs = blobs;
    private readonly ILogger<BuildDataExportsHandler> _logger = logger;

    /// <summary>Runs one pass. Returns the number of exports built to ready (0 when the claim was lost).</summary>
    /// <param name="nowUtc">Current UTC time; defines the claim window and the retention cutoff.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The number of exports that became ready; zero when the window claim was lost.</returns>
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
                        LogUserGone(_logger, row.Id);
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
                    LogExportFailed(_logger, ex, row.Id);
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
                LogBlobDeleteFailed(_logger, ex, item.Id);
                // keep the row: next pass retries (DeleteIfExists is idempotent), so a zip never outlives its row
                continue;
            }
            await _store.DeleteAsync(item.Id, ct);
        }
    }

    /// <summary>Truncate a UTC DateTime to its 5-minute boundary (TA-6.1 JobRun window key).</summary>
    /// <param name="utc">Any UTC time.</param>
    /// <returns><paramref name="utc"/> rounded down to the previous five-minute mark, with UTC kind.</returns>
    public static DateTime TruncateTo5Min(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 5) * 5, 0, DateTimeKind.Utc);

    [LoggerMessage(Level = LogLevel.Information, Message = "f-data-export: user gone, failing export {ExportId}")]
    private static partial void LogUserGone(ILogger logger, Guid exportId);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-data-export failed for export {ExportId}")]
    private static partial void LogExportFailed(ILogger logger, Exception exception, Guid exportId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-data-export: blob delete failed for export {ExportId}")]
    private static partial void LogBlobDeleteFailed(ILogger logger, Exception exception, Guid exportId);
}
