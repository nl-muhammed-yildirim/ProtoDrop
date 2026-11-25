using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Correlation;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-017 (TA-4.2#7, TA-7.2): mint a 30-minute read SAS on the whole-transfer zip; increment DownloadsCount + insert a
/// DownloadEvent row at mint time (fileId = null for download-all); flip Status→3 idempotently when the cap is reached.
/// If the zip doesn't exist yet, trigger f-zip generation via fire-and-forget HTTP POST and return Generating (202).
/// Reuses T-003-04's unlock-token verification; D6 rule: status wins over credentials.
/// </summary>
public sealed partial class GetDownloadAllUrlHandler : IRequestHandler<GetDownloadAllUrlQuery, DownloadAllResult>
{
    private readonly IPublicTransferStore _store;
    private readonly IBlobSasMinter _minter;
    private readonly IBlobStore _blobStore;
    private readonly IUnlockTokenMinter _tokens;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IConfiguration _config;
    private readonly ILogger<GetDownloadAllUrlHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="GetDownloadAllUrlHandler"/> class.</summary>
    /// <param name="store">Store that reads the transfer and records the download.</param>
    /// <param name="minter">Mints the 30-minute read URL of the zip.</param>
    /// <param name="blobStore">Blob store used to check whether the zip exists.</param>
    /// <param name="tokens">Verifies the unlock token of password-protected transfers.</param>
    /// <param name="telemetry">Reporter whose PII slots hash the client IP and User-Agent.</param>
    /// <param name="config">Configuration source for the zip function URL and signing key.</param>
    /// <param name="logger">Logger for zip-trigger warnings and failures.</param>
    public GetDownloadAllUrlHandler(
        IPublicTransferStore store,
        IBlobSasMinter minter,
        IBlobStore blobStore,
        IUnlockTokenMinter tokens,
        WaTelemetryReporter telemetry,
        IConfiguration config,
        ILogger<GetDownloadAllUrlHandler> logger)
    {
        _store = store;
        _minter = minter;
        _blobStore = blobStore;
        _tokens = tokens;
        _telemetry = telemetry;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DownloadAllResult> Handle(GetDownloadAllUrlQuery request, CancellationToken cancellationToken)
    {
        var (transfer, _) = await _store.GetByLinkIdAsync(request.LinkId, cancellationToken);
        if (transfer is not null && !IsWellFormedLinkId(request.LinkId))
            transfer = null;

        // D6: status wins over credentials — terminal rows answer their canonical states before any token logic.
        if (transfer is null || transfer.Status == 4)
            return new DownloadAllResult(DownloadAllState.NotFound, SenderEmail: null);

        if (transfer.Status == 2)
            return new DownloadAllResult(DownloadAllState.Expired, SenderEmail: transfer.SenderEmail);

        if (transfer.Status == 3)
            return new DownloadAllResult(DownloadAllState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // T-003-04 (TA-9.6#1): a valid ?t= token flips the password gate; an invalid/expired/wrong-sub/junk token behaves
        // EXACTLY like an absent one — but a gated transfer answers 401 UNAUTHENTICATED.
        var gated = transfer.PasswordHash is not null;
        if (gated && _tokens.Verify(request.Token, request.LinkId) == false)
            return new DownloadAllResult(DownloadAllState.Unauthenticated);

        // Check if the zip already exists in blob storage.
        var zipPath = BlobPaths.TransferZip(transfer.Id);
        bool zipExists;
        try
        {
            zipExists = await _blobStore.ExistsAsync(zipPath, cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 503)
        {
            // T-013-03 (FR-013-3): blob account down at mint time — per-file 503 + code INTERNAL (no new closed-list code).
            return new DownloadAllResult(DownloadAllState.StorageUnavailable);
        }

        if (zipExists)
        {
            // TA-7.2 step 4: the atomic cap check — store-owned (guarded increment + idempotent flip).
            var record = await _store.RecordZipDownloadAsync(
                transfer.Id, IpHash(request.ClientIp), UaHash(request.UserAgent), wa.domain.Analytics.BrowserFamily.FromUserAgent(request.UserAgent), cancellationToken);
            if (record.Outcome == SingleFileDownloadOutcome.CapReached)
                return new DownloadAllResult(DownloadAllState.DownloadLimit, SenderEmail: transfer.SenderEmail);

            // Mint the 30-minute read SAS on the zip blob.
            var url = _minter.MintRecipientZip(transfer.Id.ToString("n"));

            // TA-4.2#7 Emits: download_started at mint time — the browser streams directly from storage.
            _telemetry.LogEvent(TelemetryEvents.DownloadStarted);

            return new DownloadAllResult(DownloadAllState.Ok, Url: url);
        }

        // Zip doesn't exist yet — trigger f-zip generation (fire-and-forget) and return Generating (202).
        _ = Task.Run(() => TriggerZipGeneration(transfer.Id, cancellationToken));

        return new DownloadAllResult(DownloadAllState.Generating, PollAfterSec: 2);
    }

    /// <summary>
    /// Fire-and-forget HTTP POST to the f-zip Azure Function. The function handles auth (x-zip-sign), transfer lookup,
    /// cap check, de-dup names, streaming generation, and existence check (returns existing zip if present).
    /// If the trigger fails, it's non-fatal — the zip will be generated by whoever gets there first (idempotent).
    /// </summary>
    private async Task TriggerZipGeneration(Guid transferId, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _config["Wa:ZipFunction:BaseUrl"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                LogZipBaseUrlNotConfigured(_logger, transferId);
                return;
            }

            var signKey = _config["Wa:Zip:SignKey"] ?? string.Empty;
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/zip/{transferId.ToString("n")}");
            request.Headers.Add("x-zip-sign", signKey);
            // T-012-01: forward the ambient W3C traceparent so f-zip's logs share this request's correlationId (TA-10.1).
            request.Headers.Add("traceparent", CorrelationContext.CurrentRequest?.ToHeader() ?? W3CTraceParser.New().ToHeader());

            await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal: the zip will be generated by whoever gets there first (idempotent).
            LogZipTriggerFailed(_logger, ex, transferId);
        }
    }

    /// <summary>TA-9.4: IP → HMAC-SHA256 keyed by Wa:Jwt:Secret bytes (the reporter's designated slot; null stays null).</summary>
    private string? IpHash(string? clientIp) =>
        string.IsNullOrEmpty(clientIp) ? null : _telemetry.IpSlot(clientIp);

    /// <summary>TA-9.4: User-Agent → HMAC-SHA256 lowercase hex keyed like IpHash (exactly 64 chars — fits UaHash VARCHAR(64); null stays null).</summary>
    private string? UaHash(string? userAgent) =>
        string.IsNullOrEmpty(userAgent) ? null : _telemetry.IpSlot(userAgent); // IpSlot IS the keyed HMAC-SHA256 hex slot (== PiiTransforms.UserAgent) — keeps the reporter's closed slot set unchanged

    /// <summary>8 chars from the LinkId charset (TA-9.6) — same filter as GetPublicTransferHandler's private helper.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wa:ZipFunction:BaseUrl not configured — cannot trigger zip generation for transfer {TransferId}")]
    private static partial void LogZipBaseUrlNotConfigured(ILogger logger, Guid transferId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to trigger zip generation for transfer {TransferId}")]
    private static partial void LogZipTriggerFailed(ILogger logger, Exception exception, Guid transferId);
}
