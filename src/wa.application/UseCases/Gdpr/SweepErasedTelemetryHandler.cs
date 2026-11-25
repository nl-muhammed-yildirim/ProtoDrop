using Microsoft.Extensions.Logging;

namespace wa.application.UseCases.Gdpr;

/// <summary>
/// US-047-02 (FR-047-5/8, TA-6.2): f-gdpr-sweep handler. Erases telemetry of accounts deleted more than 24 h ago and marks
/// their Erasure marker swept. Idempotent per userId (a swept marker is never due again); emits no event (FR-047-8).
/// No log template carries a userId — logging it would itself create a userId-keyed trace after the sweep.
/// </summary>
/// <param name="store">Store that claims the window and tracks pending markers.</param>
/// <param name="eraser">Port that erases the telemetry of one user.</param>
/// <param name="logger">Logger for diagnostics (never logs user ids).</param>
public sealed partial class SweepErasedTelemetryHandler(IGdprSweepStore store, ITelemetryEraser eraser, ILogger<SweepErasedTelemetryHandler> logger)
{
    private readonly IGdprSweepStore _store = store;
    private readonly ITelemetryEraser _eraser = eraser;
    private readonly ILogger<SweepErasedTelemetryHandler> _logger = logger;

    /// <summary>Returns the number of markers swept (0 when the claim was lost or nothing is due).</summary>
    /// <param name="nowUtc">Current UTC time; defines the claim window and the 24-hour cutoff.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The resulting numeric value.</returns>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var windowStart = TruncateToHour(nowUtc);
        if (!await _store.TryClaimAsync(windowStart, nowUtc.AddMinutes(30), ct))
            return 0;

        try
        {
            var due = await _store.GetDueAsync(nowUtc - GdprSweepPolicy.Grace, GdprSweepPolicy.BatchSize, ct);
            var swept = 0;
            for (var i = 0; i < due.Count; i++)
            {
                var userId = due[i];
                try
                {
                    await _eraser.EraseUserAsync(userId, nowUtc - GdprSweepPolicy.Retention, ct);
                    await _store.MarkSweptAsync(userId, nowUtc, ct);
                    swept++;
                }
                catch (TelemetryUnavailableException)
                {
                    // EC-047-4: telemetry store down — mark nothing more, the next night retries.
                    LogTelemetryStoreUnavailable(_logger, due.Count - i);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogEraseFailed(_logger, ex);
                }
            }

            return swept;
        }
        finally
        {
            await _store.CompleteClaimAsync(windowStart, ct);
        }
    }

    /// <summary>Truncate a UTC DateTime to the hour boundary (TA-6.1 JobRun window key).</summary>
    /// <param name="utc">Any UTC time.</param>
    /// <returns><paramref name="utc"/> rounded down to the full hour, with UTC kind.</returns>
    public static DateTime TruncateToHour(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-gdpr-sweep: telemetry store unavailable, {Count} markers stay pending")]
    private static partial void LogTelemetryStoreUnavailable(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-gdpr-sweep: erase failed, marker stays pending")]
    private static partial void LogEraseFailed(ILogger logger, Exception exception);
}
