using MediatR;
using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>One staged file of a draft (TA-4.2#1 request row). POCO with settable properties for minimal-API binding — camelCase JSON binds to PascalCase via the default binder.</summary>
public sealed class CreateDraftFile
{
    public string Name { get; set; } = null!;

    /// <summary>Size in bytes (TA-4.2#1: <c>sizeBytes</c>).</summary>
    public long SizeBytes { get; set; }

    public string? ContentType { get; set; }
}

/// <summary>POST /api/v1/transfers/draft request — the full staged-file selection (TA-4.2#1).</summary>
public sealed record CreateDraftCommand(IReadOnlyList<CreateDraftFile> Files) : IRequest<CreateDraftResult>;

/// <summary>Per-file mint outcome: the browser uploads to <see cref="UploadUrl"/> under its own identity.</summary>
public sealed record DraftFile(Guid FileId, Uri UploadUrl);

/// <summary>POST /api/v1/transfers/draft result (TA-4.2#1 response shape — the endpoint re-flattens this into its anonymous body).</summary>
public sealed record CreateDraftResult(Guid DraftId, int ExpiresInSec, IReadOnlyList<DraftFile> Files);

/// <summary>
/// TA-4.2a — the only business logic behind POST /api/v1/transfers/draft: resolve guest limits (never literals), check the whole selection against MAX_TRANSFER_SIZE and per-file MAX_SINGLE_FILE, mint one staging SAS per file, emit upload_started once. Returns null when oversized — the endpoint throws BusinessException(ProblemCodes.TransferSizeExceeded) from there; no new tables/migrations (T-001-05).
/// </summary>
public sealed class CreateDraftHandler : IRequestHandler<CreateDraftCommand, CreateDraftResult>
{
    private readonly ILimitsProvider _limits;
    private readonly IBlobSasMinter _minter;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<CreateDraftHandler> _logger;

    public CreateDraftHandler(ILimitsProvider limits, IBlobSasMinter minter, WaTelemetryReporter telemetry, ILogger<CreateDraftHandler> logger)
    {
        _limits = limits;
        _minter = minter;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<CreateDraftResult> Handle(CreateDraftCommand request, CancellationToken cancellationToken)
    {
        var free = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken); // DB failure / unknown plan propagate (AC-053-4, EC-053-3).

        long totalBytes = 0;
        foreach (var file in request.Files)
            totalBytes += file.SizeBytes;

        bool oversizedTotal = totalBytes > free.MaxTransferSize; // strict: exactly-at-limit passes (boundary pinned in unit tests).
        int overIndex = -1;
        for (int i = 0; i < request.Files.Count; i++)
        {
            if (request.Files[i].SizeBytes <= free.MaxSingleFile) continue;
            overIndex = i;
            break;
        }

        if (oversizedTotal || overIndex >= 0)
        {
            // TA-9.4: file names never enter a log line raw — SHA-256 slot via the reporter's PII transform.
            var detail = overIndex < 0
                ? $"total {totalBytes} exceeds transfer limit {free.MaxTransferSize}"
                : $"file '{_telemetry.FileNameSlot(request.Files[overIndex].Name)}' is {request.Files[overIndex].SizeBytes} bytes, limit is {free.MaxSingleFile}";
            _logger.LogWarning("Draft rejected: {Detail}", detail);

            throw new TransferSizeExceededException(
                totalBytes, free.MaxTransferSize,
                overIndex >= 0 ? request.Files[overIndex].SizeBytes : 0,
                free.MaxSingleFile);
        }

        var draftId = Guid.NewGuid();
        var files = new List<DraftFile>(request.Files.Count);
        foreach (var file in request.Files)
        {
            // Fresh fileId per staged file — the canonical staging path is /staging/{draftId:N}/f/{fileId:N} (TA-3.5).
            var fileId = Guid.NewGuid();
            files.Add(new DraftFile(fileId, _minter.MintDraftUpload(draftId.ToString("n"), fileId.ToString("n"))));
        }

        // TA-10.2 closed name — the only event this use case emits (T-056's pipeline pins its template).
        _telemetry.LogEvent(TelemetryEvents.UploadStarted);

        return new CreateDraftResult(draftId, 7200, files); // 7200 = TA-3.6's 2 h draft-upload TTL in seconds (constant, not a plan limit).
    }
}

