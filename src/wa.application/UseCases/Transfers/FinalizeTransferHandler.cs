using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// TA-4.2a — the only business logic behind POST /api/v1/transfers/draft/{draftId}/finalize:
/// idempotent finalize (EC-002-1), server-side blob copy staging→transfers, create Transfer(Draft) + FileItems + BlobRefs in one transaction, emit upload_completed.
/// Returns null when zero files — the endpoint throws BusinessException(ProblemCodes.TransferNotFinalized).
/// </summary>
public sealed class FinalizeTransferHandler : IRequestHandler<FinalizeTransferCommand, FinalizeTransferResult?>
{
    private readonly ITransferStore _store;
    private readonly IBlobStore _blobStore;
    private readonly IPlanContextAccessor _planContext;
    private readonly string _publicUrl;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<FinalizeTransferHandler> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="FinalizeTransferHandler"/> class.</summary>
    /// <param name="store">Store that persists the draft transfer and its files.</param>
    /// <param name="blobStore">Blob store used to copy staged blobs into the transfer.</param>
    /// <param name="planContext">Accessor for the request's plan limits.</param>
    /// <param name="configuration">Configuration source for the public site URL used in the result.</param>
    /// <param name="telemetry">Reporter for the upload and storage telemetry.</param>
    /// <param name="logger">Logger injected for the handler (currently unused).</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public FinalizeTransferHandler(
        ITransferStore store,
        IBlobStore blobStore,
        IPlanContextAccessor planContext,
        IConfiguration configuration,
        WaTelemetryReporter telemetry,
        ILogger<FinalizeTransferHandler> logger,
        TimeProvider timeProvider)
    {
        _store = store;
        _blobStore = blobStore;
        _planContext = planContext;
        _publicUrl = configuration["Wa:Url:Public"] ?? "http://localhost:5173";
        _telemetry = telemetry;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<FinalizeTransferResult?> Handle(FinalizeTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Idempotency check first (EC-002-1): if the key exists and is not expired, return the stored result.
        var existing = await _store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null && existing.ExpiresAtUtc > _timeProvider.GetUtcNow().UtcDateTime)
        {
            var cached = JsonSerializer.Deserialize<FinalizeTransferResult>(existing.ResultJson!);
            return cached;
        }

        // 2. Zero-files guard: endpoint maps null → BusinessException(ProblemCodes.TransferNotFinalized).
        if (request.Files.Count == 0)
            return null;

        // 3. Mint LinkId with collision retry (TA-7.1): regenerate once; second collision → InvalidOperationException (maps to 500 INTERNAL).
        var linkId = wa.domain.ValueObjects.LinkId.Mint();
        if (await _store.TransferExistsByLinkIdAsync(linkId.Value, cancellationToken))
        {
            linkId = wa.domain.ValueObjects.LinkId.Mint();
            if (await _store.TransferExistsByLinkIdAsync(linkId.Value, cancellationToken))
                throw new InvalidOperationException($"LinkId collision after retry: {linkId.Value}");
        }

        var transferId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var totalBytes = request.Files.Sum(f => f.SizeBytes);
        var totalRetries = request.Files.Sum(f => f.Retries);

        // T-007-03: account-level checks (guests skip — EC-007-3 / FR-020-4).
        // T-007-05: activeBytes is the quota law's read and the meter's source — same value, no second query.
        long? activeBytes = null;
        if (request.OwnerAppUserId is not null)
        {
            var limits = (await _planContext.GetAsync(cancellationToken)).Limits; // US-020-02 (FR-020-2): the session user's current plan

            activeBytes = await _store.GetActiveStorageBytesAsync(request.OwnerAppUserId.Value, cancellationToken);
            if (activeBytes.Value + totalBytes > limits.StorageQuota)
                throw new StorageQuotaExceededException(activeBytes.Value, totalBytes, limits.StorageQuota);

            int activeCount = await _store.GetActiveTransferCountAsync(request.OwnerAppUserId.Value, cancellationToken);
            if (!limits.IsActiveTransfersMaxInfinite() && activeCount >= limits.ActiveTransfersMax)
                throw new ActiveTransfersExceededException(activeCount, limits.ActiveTransfersMax);
        }

        // 4. Blob copy FIRST (before any DB writes): staging → transfers. If a blob is missing, the whole finalize fails cleanly with no DB side effects.
        var stopwatch = Stopwatch.StartNew();
        foreach (var file in request.Files)
        {
            var stagingPath = BlobPaths.StagingFile(request.DraftId, file.FileId);
            var transferPath = BlobPaths.TransferFile(transferId, file.FileId);

            // Server-side copy — no bytes pass through this process. The old download→MemoryStream→upload path
            // buffered the whole blob in RAM: a 2.5 GB file blew past Int32.MaxValue (MemoryStream's limit) with
            // "Stream was too long" and timed out on smaller-but-slow copies. Copy + metadata size read scale to any size.
            await _blobStore.CopyAsync(stagingPath, transferPath, cancellationToken);

            // T-007-03: blob size truth — declared SizeBytes must match actual (±0 tolerance). Metadata read only.
            var actualSize = await _blobStore.GetSizeAsync(transferPath, cancellationToken);
            if (actualSize != file.SizeBytes)
                throw new BlobSizeMismatchException(file.Name, file.SizeBytes, actualSize);
        }
        stopwatch.Stop();

        // 5. Create entities in a transaction.
        var transfer = new Transfer
        {
            Id = transferId,
            LinkId = linkId.Value,
            Status = 0, // Draft
            ExpiresAtUtc = now.AddDays(30),
            TotalBytes = totalBytes,
            FileCount = request.Files.Count,
            SenderName = "Guest",
            MaxDownloads = 1,
            OwnerAppUserId = request.OwnerAppUserId, // T-055-02 (TA-9.1) / FR-008-7: owned rows must carry their owner from finalize — SendTransferHandler (SendTransfer.cs L164-L165) reads transfer.OwnerAppUserId for the storage meter; lands ahead of T-008-08 which assumes rows already have it.
            CreatedAtUtc = now
        };

        var blobRefs = new List<BlobRef>(request.Files.Count);
        var fileItems = new List<FileItem>(request.Files.Count);

        for (var i = 0; i < request.Files.Count; i++)
        {
            var file = request.Files[i];
            var blobRef = new BlobRef
            {
                Id = Guid.NewGuid(),
                BlobPath = BlobPaths.TransferFile(transferId, file.FileId),
                SizeBytes = file.SizeBytes,
                RefCount = 1,
                CreatedAtUtc = now
            };
            blobRefs.Add(blobRef);

            var fileItem = new FileItem
            {
                // T-003-09-fix: reuse the draft fileId — finalize copies blobs to BlobPaths.TransferFile(transferId, file.FileId),
                // so endpoint 6's minted SAS (keyed on FileItem.Id) only resolves when FileItem.Id == draft fileId. The fresh
                // Guid.NewGuid() here minted for a path that never exists → EC-003-1 / all real downloads 404'd.
                Id = file.FileId,
                TransferId = transferId,
                BlobRefId = blobRef.Id,
                OriginalName = file.Name,
                SizeBytes = file.SizeBytes,
                ContentType = file.ContentType,
                SortOrder = i
            };
            fileItems.Add(fileItem);
        }

        var result = new FinalizeTransferResult(
            transferId,
            linkId.Value,
            $"{_publicUrl}/t/{linkId.Value}",
            0, // Draft
            now.AddDays(30),
            totalBytes,
            request.Files.Select(f => new FinalizedFile(f.FileId, f.Name, f.SizeBytes, f.ContentType)).ToList());

        var idempotencyKey = new IdempotencyKey
        {
            Key = request.IdempotencyKey,
            ResultJson = JsonSerializer.Serialize(result),
            TransferId = transferId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24),
            StatusCode = 201 // T-002-02: middleware replays this status on idempotent hit
        };

        await _store.SaveFinalizeAsync(transfer, blobRefs, fileItems, idempotencyKey, cancellationToken);

        // 6. Telemetry: upload_completed with bytes, durationMs (blob copy only), total retries.
        _telemetry.UploadCompleted(totalBytes, stopwatch.ElapsedMilliseconds, totalRetries);

        // T-007-05 / AC-007-2 (meter): storage_bytes_active — the law's exact comparison value.
        // The new row is Draft (Status=0), not yet inside the SUM expression, so prior + this transfer is FR-007-4's read.
        if (request.OwnerAppUserId is not null)
            _telemetry.StorageBytesActive(activeBytes!.Value + totalBytes);

        return result;
    }
}
