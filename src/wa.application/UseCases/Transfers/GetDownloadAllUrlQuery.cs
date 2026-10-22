using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-004-02 (TA-4.2#7): GET /api/v1/public/transfers/{linkId}/download-all-url?t= — mint a 30-minute read SAS on the
/// whole-transfer zip if it exists, otherwise trigger f-zip generation (fire-and-forget) and return 202 with pollAfterSec.
/// Token = the ?t= unlock JWT (T-003-04, TA-9.6#1); ClientIp/UserAgent are raw request values — transformed here via the
/// reporter's PII slots (TA-9.4).
/// </summary>
public sealed record GetDownloadAllUrlQuery(
    string LinkId,
    string? Token = null,
    string? ClientIp = null,
    string? UserAgent = null) : IRequest<DownloadAllResult>;

/// <summary>The download-all endpoint's state mapping (TA-4.2#7): terminal states + the auth gate + zip ready/generating.</summary>
public enum DownloadAllState { Ok, Generating, Expired, DownloadLimit, NotFound, Unauthenticated }

/// <summary>Result of GetDownloadAllUrlQuery — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a).</summary>
public sealed record DownloadAllResult(
    DownloadAllState State,
    Uri? Url = null,          // Ok only — the 30-minute SAS on the zip blob
    int? PollAfterSec = null, // Generating only — how long to wait before re-polling
    string? SenderEmail = null); // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)

/// <summary>
/// T-017 (TA-4.2#7, TA-7.2): mint a 30-minute read SAS on the whole-transfer zip; increment DownloadsCount + insert a
/// DownloadEvent row at mint time (fileId = null for download-all); flip Status→3 idempotently when the cap is reached.
/// If the zip doesn't exist yet, trigger f-zip generation via fire-and-forget HTTP POST and return Generating (202).
/// Reuses T-003-04's unlock-token verification; D6 rule: status wins over credentials.
/// </summary>
public sealed class GetDownloadAllUrlHandler : IRequestHandler<GetDownloadAllUrlQuery, DownloadAllResult>
{
    private readonly IPublicTransferStore _store;
    private readonly IBlobSasMinter _minter;
    private readonly IBlobStore _blobStore;
    private readonly IUnlockTokenMinter _tokens;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IConfiguration _config;
    private readonly ILogger<GetDownloadAllUrlHandler> _logger;

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
        var zipExists = await _blobStore.ExistsAsync(zipPath, cancellationToken);

        if (zipExists)
        {
            // TA-7.2 step 4: the atomic cap check — store-owned (guarded increment + idempotent flip).
            var record = await _store.RecordZipDownloadAsync(
                transfer.Id, IpHash(request.ClientIp), UaHash(request.UserAgent), cancellationToken);
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
                _logger.LogWarning("Wa:ZipFunction:BaseUrl not configured — cannot trigger zip generation for transfer {TransferId}", transferId);
                return;
            }

            var signKey = _config["Wa:Zip:SignKey"] ?? string.Empty;
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/zip/{transferId.ToString("n")}");
            request.Headers.Add("x-zip-sign", signKey);

            await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal: the zip will be generated by whoever gets there first (idempotent).
            _logger.LogWarning(ex, "Failed to trigger zip generation for transfer {TransferId}", transferId);
        }
    }

    /// <summary>TA-9.4: IP → HMAC-SHA256 keyed by Wa:Jwt:Secret bytes (the reporter's designated slot; null stays null).</summary>
    private string? IpHash(string? clientIp) =>
        string.IsNullOrEmpty(clientIp) ? null : _telemetry.IpSlot(clientIp);

    /// <summary>TA-9.4: User-Agent → SHA-256 lowercase hex (exactly 64 chars — fits UaHash VARCHAR(64); null stays null).</summary>
    private static string? UaHash(string? userAgent) =>
        string.IsNullOrEmpty(userAgent) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userAgent))).ToLowerInvariant();

    /// <summary>8 chars from the LinkId charset (TA-9.6) — same filter as GetPublicTransferHandler's private helper.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));
}
