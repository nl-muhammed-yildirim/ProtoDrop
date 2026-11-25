using MediatR;
using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// TA-4.2a — the only business logic behind POST /api/v1/transfers/draft: resolve the request's plan limits via PlanContext (never literals), check the whole selection against MAX_TRANSFER_SIZE and per-file MAX_SINGLE_FILE, mint one staging SAS per file, emit upload_started once. Returns null when oversized — the endpoint throws BusinessException(ProblemCodes.TransferSizeExceeded) from there; no new tables/migrations (T-001-05).
/// </summary>
public sealed class CreateDraftHandler : IRequestHandler<CreateDraftCommand, CreateDraftResult>
{
    private readonly IPlanContextAccessor _planContext;
    private readonly IBlobSasMinter _minter;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<CreateDraftHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="CreateDraftHandler"/> class.</summary>
    /// <param name="planContext">The plan context.</param>
    /// <param name="minter">The minter.</param>
    /// <param name="telemetry">The telemetry.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public CreateDraftHandler(IPlanContextAccessor planContext, IBlobSasMinter minter, WaTelemetryReporter telemetry, ILogger<CreateDraftHandler> logger)
    {
        _planContext = planContext;
        _minter = minter;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CreateDraftResult> Handle(CreateDraftCommand request, CancellationToken cancellationToken)
    {
        var free = (await _planContext.GetAsync(cancellationToken)).Limits; // FR-020-1: the session's plan (guests resolve to Free via the accessor); DB failure / unknown plan propagate (AC-053-4, EC-053-3).

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
