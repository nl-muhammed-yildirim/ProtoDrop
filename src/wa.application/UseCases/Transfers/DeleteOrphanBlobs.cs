using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-005-03: f-delete-blobs timer job handler (TA-6.5). Selects BlobRef rows with RefCount=0 AND PhysicallyDeletedAtUtc &lt; now,
/// deletes the blob from storage (swallowing BlobNotFound), then removes the row. Hourly JobRun claim (TA-6.1).
/// </summary>
public sealed class DeleteOrphanBlobsHandler
{
    private const int BatchSize = 200; // TA-6.5 — no explicit batch size in spec; 200 is a reasonable default for hourly sweep

    private readonly IBlobDeletionStore _store;
    private readonly IBlobStore _blobStore;
    private readonly ILogger<DeleteOrphanBlobsHandler> _logger;

    public DeleteOrphanBlobsHandler(
        IBlobDeletionStore store,
        IBlobStore blobStore,
        ILogger<DeleteOrphanBlobsHandler> logger)
    {
        _store = store;
        _blobStore = blobStore;
        _logger = logger;
    }

    /// <summary>Run the blob deletion job. Returns the number of blobs deleted (0 if claim was lost or no rows).</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        // 1. Claim the hourly window via JobRun composite PK.
        var runWindowStart = TruncateToHour(nowUtc);
        if (!await _store.TryClaimAsync(runWindowStart, nowUtc.AddMinutes(30), ct))
            return 0;

        try
        {
            // 2. Scan in batches until exhausted.
            var totalDeleted = 0;

            while (true)
            {
                var batch = await _store.GetNextBatchAsync(nowUtc, BatchSize, ct);
                if (batch.Count == 0) break;

                foreach (var blob in batch)
                {
                    // 3. Safety: skip paths not starting with "transfers/" — log UNEXPECTED_BLOB_PATH.
                    if (!blob.BlobPath.StartsWith("transfers/", StringComparison.Ordinal))
                    {
                        _logger.LogWarning("UNEXPECTED_BLOB_PATH: {BlobPath} does not start with 'transfers/'", blob.BlobPath);
                        continue;
                    }

                    // 4. Delete the blob (swallow BlobNotFound — EC-005-2: lifecycle safety net may have swept it).
                    try
                    {
                        await _blobStore.DeleteAsync(blob.BlobPath, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "f-delete-blobs: blob delete failed for {BlobPath}", blob.BlobPath);
                    }

                    // 5. Remove the row (always proceeds even if blob was already gone).
                    await _store.DeleteRowAsync(blob.Id, ct);
                }

                totalDeleted += batch.Count;
            }

            return totalDeleted;
        }
        finally
        {
            await _store.CompleteClaimAsync(runWindowStart, ct);
        }
    }

    /// <summary>Truncate a UTC DateTime to the nearest hour boundary (TA-6.1 JobRun window key for hourly jobs).</summary>
    public static DateTime TruncateToHour(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
}
