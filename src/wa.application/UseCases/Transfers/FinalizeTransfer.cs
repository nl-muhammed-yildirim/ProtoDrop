using System.Diagnostics;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>One file in the finalize request (TA-4.2#2 request row). POCO with settable properties for minimal-API binding.</summary>
public sealed class FinalizeFile
{
    public Guid FileId { get; set; }
    public string Name { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int Retries { get; set; }
}

/// <summary>POST /api/v1/transfers/draft/{draftId}/finalize request (TA-4.2#2).</summary>
public sealed record FinalizeTransferCommand(Guid DraftId, string IdempotencyKey, IReadOnlyList<FinalizeFile> Files) : IRequest<FinalizeTransferResult?>;

/// <summary>A file in the finalized transfer result.</summary>
public sealed record FinalizedFile(Guid FileId, string Name, long SizeBytes, string? ContentType);

/// <summary>POST /api/v1/transfers/draft/{draftId}/finalize result (TA-4.2#2 response shape).</summary>
public sealed record FinalizeTransferResult(
    Guid TransferId,
    string LinkId,
    string PublicUrl,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    IReadOnlyList<FinalizedFile> Files);

/// <summary>
/// TA-4.2a — the only business logic behind POST /api/v1/transfers/draft/{draftId}/finalize:
/// idempotent finalize (EC-002-1), server-side blob copy staging→transfers, create Transfer(Draft) + FileItems + BlobRefs in one transaction, emit upload_completed.
/// Returns null when zero files — the endpoint throws BusinessException(ProblemCodes.TransferNotFinalized).
/// </summary>
public sealed class FinalizeTransferHandler : IRequestHandler<FinalizeTransferCommand, FinalizeTransferResult?>
{
    private readonly ITransferStore _store;
    private readonly IBlobStore _blobStore;
    private readonly string _publicUrl;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<FinalizeTransferHandler> _logger;

    public FinalizeTransferHandler(
        ITransferStore store,
        IBlobStore blobStore,
        IConfiguration configuration,
        WaTelemetryReporter telemetry,
        ILogger<FinalizeTransferHandler> logger)
    {
        _store = store;
        _blobStore = blobStore;
        _publicUrl = configuration["Wa:Url:Public"] ?? "http://localhost:5173";
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<FinalizeTransferResult?> Handle(FinalizeTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Idempotency check first (EC-002-1): if the key exists and is not expired, return the stored result.
        var existing = await _store.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null && existing.ExpiresAtUtc > DateTime.UtcNow)
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
        var now = DateTime.UtcNow;
        var totalBytes = request.Files.Sum(f => f.SizeBytes);
        var totalRetries = request.Files.Sum(f => f.Retries);

        // 4. Blob copy FIRST (before any DB writes): staging → transfers. If a blob is missing, the whole finalize fails cleanly with no DB side effects.
        var stopwatch = Stopwatch.StartNew();
        foreach (var file in request.Files)
        {
            var stagingPath = BlobPaths.StagingFile(request.DraftId, file.FileId);
            var transferPath = BlobPaths.TransferFile(transferId, file.FileId);

            await using var stream = await _blobStore.DownloadAsync(stagingPath, cancellationToken);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            await _blobStore.UploadAsync(transferPath, ms, cancellationToken);
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

        return result;
    }
}
