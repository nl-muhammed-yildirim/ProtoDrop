using MediatR;
using Microsoft.Extensions.Logging;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-016 (TA-4.2#6, TA-7.2 step 4): mint a 30-minute read SAS on one transfer file; increment DownloadsCount + insert a
/// DownloadEvent row at mint time (MVP over-count documented — F-TRF-003 technical notes); flip Status→3 idempotently when
/// the cap is reached (the flip lives in the store, not a worker). Reuses T-003-04's unlock-token verification; TA-9.6
/// enumeration safety: unknown/malformed fileId under an active transfer answers NotFound — same shape as unknown linkId.
/// </summary>
/// <param name="store">Store that reads the transfer and records the download.</param>
/// <param name="minter">Mints the 30-minute read URL of the file.</param>
/// <param name="tokens">Verifies the unlock token of password-protected transfers.</param>
/// <param name="telemetry">Reporter whose PII slots hash the client IP and User-Agent.</param>
/// <param name="logger">Logger injected for the handler (currently unused).</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class GetFileDownloadUrlHandler(
    IPublicTransferStore store,
    IBlobSasMinter minter,
    IUnlockTokenMinter tokens,
    WaTelemetryReporter telemetry,
    ILogger<GetFileDownloadUrlHandler> logger,
    TimeProvider timeProvider) : IRequestHandler<GetFileDownloadUrlQuery, FileDownloadResult>
{
    private readonly IPublicTransferStore _store = store;
    private readonly IBlobSasMinter _minter = minter;
    private readonly IUnlockTokenMinter _tokens = tokens;
    private readonly WaTelemetryReporter _telemetry = telemetry;
    private readonly ILogger<GetFileDownloadUrlHandler> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<FileDownloadResult> Handle(GetFileDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var (transfer, files) = await _store.GetByLinkIdAsync(request.LinkId, cancellationToken);
        if (transfer is not null && !IsWellFormedLinkId(request.LinkId))
            transfer = null;

        // D6: status wins over credentials — terminal rows answer their canonical states before any token logic.
        if (transfer is null || transfer.Status == 4)
            return new FileDownloadResult(FileDownloadState.NotFound, SenderEmail: null); // FR-003-9 same body as unknown linkId

        if (transfer.Status == 2)
            return new FileDownloadResult(FileDownloadState.Expired, SenderEmail: transfer.SenderEmail);

        if (transfer.Status == 3)
            return new FileDownloadResult(FileDownloadState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // TA-9.6: a malformed or missing fileId is indistinguishable from an unknown one — no enumeration via fileIds (D5).
        if (!IsWellFormedFileId(request.FileId) || !HasFile(files, request.FileId))
            return new FileDownloadResult(FileDownloadState.NotFound, SenderEmail: null);

        // Normalize the fileId once — every downstream use (store/minter/lookup) is keyed on the same Guid.
        var fileId = new Guid(request.FileId);

        // T-003-04 (TA-9.6#1): a valid ?t= token flips the password gate; an invalid/expired/wrong-sub/junk token behaves
        // EXACTLY like an absent one — but a gated transfer answers 401 UNAUTHENTICATED, never a file-list-shaped error.
        var gated = transfer.PasswordHash is not null;
        if (gated && _tokens.Verify(request.Token, request.LinkId) == false)
            return new FileDownloadResult(FileDownloadState.Unauthenticated);

        // TA-7.2 step 4: the atomic cap check — store-owned (guarded increment + idempotent flip). The handler never
        // pre-reads DownloadsCount: a stale read would race; only the UPDATE's rowsAffected is authoritative.
        var record = await _store.RecordSingleFileDownloadAsync(
            transfer.Id, fileId, IpHash(request.ClientIp), UaHash(request.UserAgent), wa.domain.Analytics.BrowserFamily.FromUserAgent(request.UserAgent), cancellationToken);
        if (record.Outcome == SingleFileDownloadOutcome.CapReached)
            return new FileDownloadResult(FileDownloadState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // EC-003-3 (RFC 5987): the original filename round-trips via resp-content-disposition — part of the string-to-sign.
        var url = _minter.MintRecipientFile(
            transfer.Id.ToString("n"), fileId.ToString("n"), BuildContentDisposition(Name(files, fileId)!));

        // TA-4.2#6 Emits: download_started at mint time — the browser streams directly from storage, so the API cannot
        // observe download_completed (M0: no client telemetry pipeline — documented deviation from step 5).
        _telemetry.LogEvent(TelemetryEvents.DownloadStarted);

        return new FileDownloadResult(FileDownloadState.Ok, Url: url, ExpiresAt: _timeProvider.GetUtcNow().AddMinutes(30));
    }

    /// <summary>TA-9.4: IP → HMAC-SHA256 keyed by Wa:Jwt:Secret bytes (the reporter's designated slot; null stays null).</summary>
    private string? IpHash(string? clientIp) =>
        string.IsNullOrEmpty(clientIp) ? null : _telemetry.IpSlot(clientIp);

    /// <summary>TA-9.4: User-Agent → HMAC-SHA256 lowercase hex keyed like IpHash (exactly 64 chars — fits UaHash VARCHAR(64); null stays null).</summary>
    private string? UaHash(string? userAgent) =>
        string.IsNullOrEmpty(userAgent) ? null : _telemetry.IpSlot(userAgent); // IpSlot IS the keyed HMAC-SHA256 hex slot (== PiiTransforms.UserAgent) — keeps the reporter's closed slot set unchanged

    /// <summary>RFC 5987: the original filename round-trips via resp-content-disposition — part of the string-to-sign.</summary>
    private static string BuildContentDisposition(string fileName) =>
        $"attachment;filename*=UTF-8''{Uri.EscapeDataString(fileName)}";

    /// <summary>8 chars from the LinkId charset (TA-9.6) — same filter as GetPublicTransferHandler's private helper.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));

    /// <summary>TA-9.6: a well-formed fileId is any Guid.Parse-able string (32-hex-char wire format or with hyphens — no enumeration via format errors).</summary>
    private static bool IsWellFormedFileId(string fileId) => Guid.TryParse(fileId, out var g) && !g.Equals(Guid.Empty);

    /// <summary>
    /// TA-9.6: the fileId must exist in this transfer (no enumeration via unknown fileIds).
    /// Compares on the parsed Guid so any well-formed format (n/D/uppercase — hyphenated or not) matches.
    /// </summary>
    private static bool HasFile(IReadOnlyList<PublicFileItem> files, string fileId) =>
        files.Any(f => f.FileId == new Guid(fileId));

    /// <summary>Returns the original filename for the given fileId (must exist).</summary>
    private static string? Name(IReadOnlyList<PublicFileItem> files, Guid fileId) =>
        files.FirstOrDefault(f => f.FileId == fileId)?.Name;
}
