using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-01: f-expire timer job handler (TA-6.3). Scans for Active transfers past their expiry and marks them Expired,
/// emitting transfer.expired per row. Claim via JobRun prevents double-processing in scale-out (FR-005-9).
/// </summary>
public sealed class ExpireTransfersHandler
{
    private const int BatchSize = 500; // TA-6.3 architecture constant, not a plan limit
    private static readonly TimeSpan SkewTolerance = TimeSpan.FromMinutes(2); // EC-005-1

    private readonly IExpiryStore _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<ExpireTransfersHandler> _logger;

    public ExpireTransfersHandler(
        IExpiryStore store,
        IEventPublisher eventPublisher,
        WaTelemetryReporter telemetry,
        ILogger<ExpireTransfersHandler> logger)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>Run the expiry job. Returns the number of transfers expired (0 if claim was lost or no rows).</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        // 1. Claim the window via JobRun composite PK.
        var runWindowStart = TruncateTo15Min(nowUtc);
        if (!await _store.TryClaimAsync(runWindowStart, nowUtc.AddMinutes(10), ct))
            return 0;

        try
        {
            // 2. Scan in batches of 500 (TA-6.3). Cutoff = now - 2min tolerance (EC-005-1).
            var cutoff = nowUtc - SkewTolerance;
            var totalExpired = 0;

            while (true)
            {
                var batch = await _store.GetNextBatchAsync(cutoff, BatchSize, ct);
                if (batch.Count == 0) break;

                // 3. Mark expired in a single transaction per batch.
                var ids = batch.Select(r => r.Id).ToList();
                var updated = await _store.MarkExpiredAsync(ids, nowUtc, ct);

                // 4. Emit transfer.expired event per row (TA-5.2/5.3).
                foreach (var row in batch)
                {
                    var envelope = new EventEnvelope
                    {
                        EventId = Guid.NewGuid(),
                        Type = EventType.TransferExpired,
                        Version = 1,
                        OccurredAtUtc = nowUtc,
                        CorrelationId = Guid.NewGuid(), // background job — no W3C trace context
                        PartitionKey = row.LinkId,
                        Payload = new { transferId = row.Id, linkId = row.LinkId }
                    };
                    await _eventPublisher.PublishAsync(envelope, ct);
                }

                totalExpired += updated;
            }

            // 5. Emit metric (TA-10.2).
            var lagSeconds = totalExpired > 0 ? (nowUtc - SkewTolerance).Ticks / TimeSpan.TicksPerSecond : 0;
            _telemetry.ExpiryJobLag(lagSeconds);

            return totalExpired;
        }
        finally
        {
            await _store.CompleteClaimAsync(runWindowStart, ct);
        }
    }

    /// <summary>Truncate a UTC DateTime to the nearest 15-minute boundary (TA-6.1 JobRun window key).</summary>
    public static DateTime TruncateTo15Min(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, (utc.Minute / 15) * 15, 0, DateTimeKind.Utc);
}
