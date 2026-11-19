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
public sealed class ActivateScheduledTransfersHandler
{
    private const int BatchSize = 500; // TA-6.3 architecture constant, not a plan limit

    private readonly IScheduledSendStore _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ActivateScheduledTransfersHandler> _logger;

    public ActivateScheduledTransfersHandler(
        IScheduledSendStore store,
        IEventPublisher eventPublisher,
        ILogger<ActivateScheduledTransfersHandler> logger)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>Run the sub-step. Returns the number of transfers activated (0 if the claim was lost or nothing was due).</summary>
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

            _logger.LogInformation("f-expire schedule: {Count} scheduled transfers activated", total);
            return total;
        }
        finally
        {
            await _store.CompleteClaimAsync(runWindowStart, DateTime.UtcNow, ct);
        }
    }
}
