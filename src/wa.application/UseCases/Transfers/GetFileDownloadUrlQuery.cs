using MediatR;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-003-06 (TA-4.2#6): GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t= — mint a 30-minute read SAS
/// on the file blob; the browser streams directly against storage (no server-side proxying). Token = the ?t= unlock JWT
/// (T-003-04, TA-9.6#1); ClientIp/UserAgent are raw request values — transformed here via the reporter's PII slots (TA-9.4).
/// </summary>
public sealed record GetFileDownloadUrlQuery(
    string LinkId,
    string FileId,
    string? Token = null,
    string? ClientIp = null,
    string? UserAgent = null) : IRequest<FileDownloadResult>;

/// <summary>The download endpoint's state mapping (TA-4.2#6): terminal states + the auth gate + mint success.</summary>
public enum FileDownloadState { Ok, Expired, DownloadLimit, NotFound, Unauthenticated }

/// <summary>Result of GetFileDownloadUrlQuery — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a).</summary>
public sealed record FileDownloadResult(
    FileDownloadState State,
    Uri? Url = null,          // Ok only — the 30-minute SAS with the original-filename Content-Disposition override (EC-003-3)
    DateTimeOffset? ExpiresAt = null, // Ok only — the SAS expiry (wire shape { url, expiresAt }, 02-feature-plan §F-TRF-003)
    string? SenderEmail = null);      // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)

/// <summary>
/// T-016 (TA-4.2#6, TA-7.2 step 4): mint a 30-minute read SAS on one transfer file; increment DownloadsCount + insert a
/// DownloadEvent row at mint time (MVP over-count documented — F-TRF-003 technical notes); flip Status→3 idempotently when
/// the cap is reached (the flip lives in the store, not a worker). Reuses T-003-04's unlock-token verification; TA-9.6
/// enumeration safety: unknown/malformed fileId under an active transfer answers NotFound — same shape as unknown linkId.
/// </summary>
public sealed class GetFileDownloadUrlHandler : IRequestHandler<GetFileDownloadUrlQuery, FileDownloadResult>
{
    private readonly IPublicTransferStore _store;
    private readonly IBlobSasMinter _minter;
    private readonly IUnlockTokenMinter _tokens;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<GetFileDownloadUrlHandler> _logger;

    public GetFileDownloadUrlHandler(
        IPublicTransferStore store,
        IBlobSasMinter minter,
        IUnlockTokenMinter tokens,
        WaTelemetryReporter telemetry,
        ILogger<GetFileDownloadUrlHandler> logger)
    {
        _store = store;
        _minter = minter;
        _tokens = tokens;
        _telemetry = telemetry;
        _logger = logger;
    }

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
            transfer.Id, fileId, IpHash(request.ClientIp), UaHash(request.UserAgent), cancellationToken);
        if (record.Outcome == SingleFileDownloadOutcome.CapReached)
            return new FileDownloadResult(FileDownloadState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // EC-003-3 (RFC 5987): the original filename round-trips via resp-content-disposition — part of the string-to-sign.
        var url = _minter.MintRecipientFile(
            transfer.Id.ToString("n"), fileId.ToString("n"), BuildContentDisposition(Name(files, fileId)!));

        // TA-4.2#6 Emits: download_started at mint time — the browser streams directly from storage, so the API cannot
        // observe download_completed (M0: no client telemetry pipeline — documented deviation from step 5).
        _telemetry.LogEvent(TelemetryEvents.DownloadStarted);

        return new FileDownloadResult(FileDownloadState.Ok, Url: url, ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
    }

    /// <summary>TA-9.4: IP → HMAC-SHA256 keyed by Wa:Jwt:Secret bytes (the reporter's designated slot; null stays null).</summary>
    private string? IpHash(string? clientIp) =>
        string.IsNullOrEmpty(clientIp) ? null : _telemetry.IpSlot(clientIp);

    /// <summary>TA-9.4: User-Agent → SHA-256 lowercase hex (exactly 64 chars — fits UaHash VARCHAR(64); null stays null).</summary>
    private static string? UaHash(string? userAgent) =>
        string.IsNullOrEmpty(userAgent) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userAgent))).ToLowerInvariant();

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
