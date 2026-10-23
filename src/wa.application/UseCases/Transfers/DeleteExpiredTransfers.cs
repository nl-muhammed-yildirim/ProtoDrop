using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-02: f-delete-transfers timer job handler (TA-6.4). Scans for Expired/DownloadLimit transfers past their grace period,
/// marks them Deleted, decrements BlobRef.RefCount per FileItem, deletes FileItem + EmailRecipient rows, and emits transfer.deleted per row.
/// Claim via JobRun prevents double-processing in scale-out (FR-005-9).
/// </summary>
public sealed class DeleteExpiredTransfersHandler
{
    private const int BatchSize = 100; // TA-6.4 architecture constant, not a plan limit
    private const int GraceDays = 3;   // TA-6.4, all plans (FR-005-3)

    private readonly IExpiryDeletionStore _store;
    private readonly IEventPublisher _eventPublisher;
    private readonly IBlobStore _blobStore;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<DeleteExpiredTransfersHandler> _logger;

    public DeleteExpiredTransfersHandler(
        IExpiryDeletionStore store,
        IEventPublisher eventPublisher,
        IBlobStore blobStore,
        WaTelemetryReporter telemetry,
        ILogger<DeleteExpiredTransfersHandler> logger)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _blobStore = blobStore;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>Run the deletion job. Returns the number of transfers deleted (0 if claim was lost or no rows).</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        // 1. Claim the window via JobRun composite PK.
        var runWindowStart = ExpireTransfersHandler.TruncateTo15Min(nowUtc);
        if (!await _store.TryClaimAsync(runWindowStart, nowUtc.AddMinutes(10), ct))
            return 0;

        try
        {
            // 2. Scan in batches of 100 (TA-6.4). Cutoff = now - GRACE_DAYS so only transfers past grace are picked up.
            var cutoff = nowUtc.AddDays(-GraceDays);
            var totalDeleted = 0;

            while (true)
            {
                var batch = await _store.GetNextBatchAsync(cutoff, BatchSize, ct);
                if (batch.Count == 0) break;

                // 3. Mark deleted in a single transaction per batch (decrements RefCount, deletes FileItem/EmailRecipient rows).
                var ids = batch.Select(r => r.Id).ToList();
                var updated = await _store.DeleteTransfersAsync(ids, nowUtc, ct);

                // 4. Delete zip blobs best-effort (swallow BlobNotFound — EC-005-2: lifecycle safety net may have swept it already).
                foreach (var id in ids)
                {
                    try
                    {
                        await _blobStore.DeleteAsync(BlobPaths.TransferZip(id), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "f-delete-transfers: zip blob delete failed for transfer {TransferId}", id);
                    }
                }

                // 5. Emit transfer.deleted event per row (TA-5.2/5.3) with reason expiry-grace.
                foreach (var row in batch)
                {
                    var envelope = new EventEnvelope
                    {
                        EventId = Guid.NewGuid(),
                        Type = EventType.TransferDeleted,
                        Version = 1,
                        OccurredAtUtc = nowUtc,
                        CorrelationId = Guid.NewGuid(), // background job — no W3C trace context
                        PartitionKey = row.LinkId,
                        Payload = new { transferId = row.Id, linkId = row.LinkId, reason = "expiry-grace" }
                    };
                    await _eventPublisher.PublishAsync(envelope, ct);
                }

                totalDeleted += updated;
            }

            return totalDeleted;
        }
        finally
        {
            await _store.CompleteClaimAsync(runWindowStart, ct);
        }
    }
}