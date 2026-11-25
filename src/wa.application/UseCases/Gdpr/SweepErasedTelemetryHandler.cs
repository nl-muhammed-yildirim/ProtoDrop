using Microsoft.Extensions.Logging;

namespace wa.application.UseCases.Gdpr;

/// <summary>
/// US-047-02 (FR-047-5/8, TA-6.2): f-gdpr-sweep handler. Erases telemetry of accounts deleted more than 24 h ago and marks
/// their Erasure marker swept. Idempotent per userId (a swept marker is never due again); emits no event (FR-047-8).
/// No log template carries a userId — logging it would itself create a userId-keyed trace after the sweep.
/// </summary>
public sealed class SweepErasedTelemetryHandler
{
    private readonly IGdprSweepStore _store;
    private readonly ITelemetryEraser _eraser;
    private readonly ILogger<SweepErasedTelemetryHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="SweepErasedTelemetryHandler"/> class.</summary>
    /// <param name="store">The store.</param>
    /// <param name="eraser">The eraser.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public SweepErasedTelemetryHandler(IGdprSweepStore store, ITelemetryEraser eraser, ILogger<SweepErasedTelemetryHandler> logger)
    {
        _store = store;
        _eraser = eraser;
        _logger = logger;
    }

    /// <summary>Returns the number of markers swept (0 when the claim was lost or nothing is due).</summary>
    /// <param name="nowUtc">The now UTC.</param>
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
                    _logger.LogWarning("f-gdpr-sweep: telemetry store unavailable, {Count} markers stay pending", due.Count - i);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "f-gdpr-sweep: erase failed, marker stays pending");
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
    /// <param name="utc">The UTC.</param>
    /// <returns>The timestamp.</returns>
    public static DateTime TruncateToHour(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
}
