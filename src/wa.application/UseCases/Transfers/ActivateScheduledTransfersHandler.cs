using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-022-04 (FR-022-5, AC-022-1, AC-022-6, EC-022-1): f-expire schedule sub-step. Flips due Scheduled transfers to Active and
/// re-emits transfer.created so f-email sends the recipient email. Claimed per 15-min window via JobRun (JobKey "schedule",
/// TA-6.1); the per-row conditional UPDATE is the second guard. The scan is stateless, so a missed run is caught by the next.
/// Ignores the feature.scheduling gate on purpose: already-scheduled rows must still go out if the gate is later switched off.
/// </summary>
public sealed partial class ActivateScheduledTransfersHandler
{
    private const int BatchSize = 500; // TA-6.3 architecture constant, not a plan limit

    private readonly IScheduledSendStore _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ActivateScheduledTransfersHandler> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ActivateScheduledTransfersHandler"/> class.</summary>
    /// <param name="store">Store that claims the window and flips due transfers to Active.</param>
    /// <param name="eventPublisher">Publishes the <c>transfer.created</c> event for each activated transfer.</param>
    /// <param name="logger">Logger for diagnostics (never logs addresses).</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public ActivateScheduledTransfersHandler(
        IScheduledSendStore store,
        IEventPublisher eventPublisher,
        ILogger<ActivateScheduledTransfersHandler> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>Run the sub-step. Returns the number of transfers activated (0 if the claim was lost or nothing was due).</summary>
    /// <param name="nowUtc">Current UTC time; defines the 15-minute claim window and which transfers are due.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The number of transfers activated; zero when the claim was lost or nothing was due.</returns>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var runWindowStart = ExpireTransfersHandler.TruncateTo15Min(nowUtc);
        if (!await _store.TryClaimAsync(runWindowStart, nowUtc.AddMinutes(10), ct))
            return 0;

        try
        {
            var total = 0;
            while (true)
            {
                var batch = await _store.GetDueBatchAsync(nowUtc, BatchSize, ct);
                if (batch.Count == 0) break;

                var activatedInBatch = 0;
                foreach (var row in batch)
                {
                    var activated = await _store.TryActivateAsync(row.Id, nowUtc, ct);
                    if (activated is null) continue; // expired, deleted or already flipped (AC-022-5, EC-022-3)

                    activatedInBatch++;
                    // The outbox writer (IEventPublisher) opens its own DbContext, so this publish cannot join the flip's transaction
                    // without changing the port; the same flip-then-publish window exists for every transfer.created emission (TA-4.1.7).
                    var envelope = new EventEnvelope
                    {
                        EventId = Guid.NewGuid(),
                        Type = EventType.TransferCreated,
                        Version = 1,
                        OccurredAtUtc = nowUtc,
                        CorrelationId = Guid.NewGuid(), // background job — no W3C trace context
                        PartitionKey = activated.LinkId,
                        Payload = new
                        {
                            transferId = activated.Id,
                            linkId = activated.LinkId,
                            totalBytes = activated.TotalBytes,
                            fileCount = activated.FileCount,
                            emails = activated.Emails,
                            senderEmail = activated.SenderEmail,
                            scheduledSendAt = (DateTime?)activated.ScheduledSendAtUtc
                        }
                    };
                    await _eventPublisher.PublishAsync(envelope, ct);
                }

                total += activatedInBatch;

                // Stop on a zero-activation batch (due-but-expired rows are left for the expiry step) or a short batch.
                if (activatedInBatch == 0 || batch.Count < BatchSize) break;
            }

            LogTransfersActivated(_logger, total);
            return total;
        }
        finally
        {
            await _store.CompleteClaimAsync(runWindowStart, _timeProvider.GetUtcNow().UtcDateTime, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "f-expire schedule: {Count} scheduled transfers activated")]
    private static partial void LogTransfersActivated(ILogger logger, int count);
}
