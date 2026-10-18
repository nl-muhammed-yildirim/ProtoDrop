using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// GET /api/v1/public/transfers/{linkId} request (TA-4.2#4). Token = the optional ?t= unlock JWT (T-003-04, TA-9.6#1) —
/// default null so all existing call sites compile unchanged; only the Program.cs endpoint 5 wiring fills it.
/// </summary>
public sealed record GetPublicTransferQuery(string LinkId, string? Token = null) : IRequest<GetPublicTransferResult>;

/// <summary>A file row of the active transfer — plain projection, no EF entity (golden rule 8: no DbContext in use cases).</summary>
public sealed record PublicFileItem(Guid FileId, string Name, long SizeBytes);

/// <summary>The recipient page's state mapping (TA-4.2#4). NotFound covers missing rows + malformed linkIds + Status=4 Deleted — deliberately indistinguishable (FR-003-9).</summary>
public enum PublicTransferState { Active, Expired, DownloadLimit, NotFound }

/// <summary>Result of GetPublicTransferQuery — the endpoint maps this to HTTP; the handler stays pure CQRS. Only the Active branch carries payload fields; terminal states never do (no byte leak).</summary>
public sealed record GetPublicTransferResult(
    PublicTransferState State,
    string? From = null,            // Active only
    string? Note = null,            // Active only — JSON null when the sender left it empty
    IReadOnlyList<PublicFileItem>? Files = null,   // Active only — never on terminal states (no byte leak)
    bool HasDownloadAll = false,    // Active only
    int? DownloadsLeft = null,      // Active only; null when the cap is ∞
    bool PasswordRequired = false,  // Active only
    string? SenderEmail = null);    // terminal bodies render it into Problem+JSON details (FR-003-8/9)

/// <summary>
/// T-014 (TA-4.2#4): recipient metadata for GET /api/v1/public/transfers/{linkId}. No auth, no cookie;
/// a page view never mints a SAS and never consumes a download (F-TRF-003 technical notes).
/// </summary>
public sealed class GetPublicTransferHandler : IRequestHandler<GetPublicTransferQuery, GetPublicTransferResult>
{
    private readonly IPublicTransferStore _store;
    private readonly ILimitsProvider _limits;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IUnlockTokenMinter _tokens;
    private readonly ILogger<GetPublicTransferHandler> _logger;

    public GetPublicTransferHandler(
        IPublicTransferStore store,
        ILimitsProvider limits,
        WaTelemetryReporter telemetry,
        IUnlockTokenMinter tokens,
        ILogger<GetPublicTransferHandler> logger)
    {
        _store = store;
        _limits = limits;
        _telemetry = telemetry;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<GetPublicTransferResult> Handle(GetPublicTransferQuery request, CancellationToken cancellationToken)
    {
        // TA-4.2#4 emits column: transfer_page_viewed on every load — the linkId hash, never the raw 8-char id (TA-9.4 style).
        _telemetry.TransferPageViewed(PiiTransforms.FileName(request.LinkId));

        // D5 post-query filter: rows matched by a linkId outside the 8-char charset are treated as unknown — no ID enumeration via format errors.
        var (transfer, files) = await _store.GetByLinkIdAsync(request.LinkId, cancellationToken);
        if (transfer is not null && !IsWellFormedLinkId(request.LinkId))
            transfer = null;

        // Terminal-state mapping: missing / malformed / Status=4 Deleted → NotFound (same body as expired, 404 instead of 410 — FR-003-9).
        if (transfer is null || transfer.Status == 4)
        {
            // transfer_not_found only on the 404 case (AC-003-4).
            _telemetry.LogEvent(TelemetryEvents.TransferNotFound);
            return new GetPublicTransferResult(PublicTransferState.NotFound, SenderEmail: null);
        }

        if (transfer.Status == 2)
            return new GetPublicTransferResult(PublicTransferState.Expired, SenderEmail: transfer.SenderEmail);

        if (transfer.Status == 3)
            return new GetPublicTransferResult(PublicTransferState.DownloadLimit, SenderEmail: transfer.SenderEmail);

        // T-003-04 (TA-9.6#1): a valid ?t= token flips passwordRequired to false — no re-prompt on refresh.
        // An invalid/expired/wrong-sub/junk token behaves EXACTLY like an absent one (flag stays true, same body — TA-9.6 enumeration safety).
        // Status already won above (D6): the token only matters in this Active branch, so a terminal state always wins over it (US-003-05).
        var gated = transfer.PasswordHash is not null;
        var passwordRequired = gated && (_tokens.Verify(request.Token, request.LinkId) == false);

        // Active — full metadata payload; no SAS minted at page view, no download consumed.
        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken); // no auth on this endpoint → guest/free plan (TA-3.4: never a literal).
        return new GetPublicTransferResult(
            PublicTransferState.Active,
            From: transfer.SenderName,
            Note: transfer.Note,
            Files: files,
            HasDownloadAll: transfer.FileCount >= 2 && transfer.TotalBytes <= limits.MaxZipSize,
            DownloadsLeft: transfer.MaxDownloads == -1 ? null : transfer.MaxDownloads - transfer.DownloadsCount, // the same column TA-7.2's flip uses — page and cap stay consistent
            PasswordRequired: passwordRequired);
    }

    /// <summary>8 chars from the LinkId charset (TA-9.6) — anything else is treated as unknown on purpose.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));
}
