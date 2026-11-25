using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

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
    private readonly ISenderBrandingStore _branding;
    private readonly IFeaturesProvider _features;
    private readonly IBlobStore _blobs;
    private readonly IOrgLogoSasMinter _logoMinter;
    private readonly IUserPlanStore _users;
    private readonly IFlagResolver _flags;
    private readonly ILogger<GetPublicTransferHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="GetPublicTransferHandler"/> class.</summary>
    /// <param name="store">Store that reads the transfer by link id.</param>
    /// <param name="limits">Provider of the guest plan's limits.</param>
    /// <param name="telemetry">Reporter for the <c>transfer_page_viewed</c> and <c>transfer_not_found</c> events.</param>
    /// <param name="tokens">Verifies the unlock token of password-protected transfers.</param>
    /// <param name="branding">Store that reads the sender's organization branding.</param>
    /// <param name="features">Provider of the sender plan's feature toggles.</param>
    /// <param name="blobs">Blob store used to check that the logo blob exists.</param>
    /// <param name="logoMinter">Mints the inline read URL of the sender's logo.</param>
    /// <param name="users">Store used to resolve the sender's plan.</param>
    /// <param name="flags">Resolver for the ads feature gate.</param>
    /// <param name="logger">Logger for page-view warnings and failures.</param>
    public GetPublicTransferHandler(
        IPublicTransferStore store,
        ILimitsProvider limits,
        WaTelemetryReporter telemetry,
        IUnlockTokenMinter tokens,
        ISenderBrandingStore branding,
        IFeaturesProvider features,
        IBlobStore blobs,
        IOrgLogoSasMinter logoMinter,
        IUserPlanStore users,
        IFlagResolver flags,
        ILogger<GetPublicTransferHandler> logger)
    {
        _store = store;
        _limits = limits;
        _telemetry = telemetry;
        _tokens = tokens;
        _branding = branding;
        _features = features;
        _blobs = blobs;
        _logoMinter = logoMinter;
        _users = users;
        _flags = flags;
        _logger = logger;
    }

    /// <inheritdoc />
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
        var branding = await SenderBranding.ResolveAsync(_branding, _features, transfer.OwnerAppUserId, cancellationToken);
        string? logoUrl = null;
        if (branding.HasLogo && transfer.OwnerAppUserId is { } ownerId)
        {
            if (await _blobs.ExistsAsync(BlobPaths.OrgLogo(ownerId), cancellationToken))
                logoUrl = _logoMinter.MintOrgLogo(ownerId).ToString();
            else
                _telemetry.LogEvent(TelemetryEvents.BrandingLogoMissing); // EC-021-2: graceful fallback to the name-only header
        }

        var adsEnabled = await RecipientAds.ResolveAsync(_flags, _users, _features, transfer.OwnerAppUserId, cancellationToken);

        return new GetPublicTransferResult(
            PublicTransferState.Active,
            From: transfer.SenderName,
            Note: transfer.Note,
            Files: files,
            HasDownloadAll: transfer.FileCount >= 2 && transfer.TotalBytes <= limits.MaxZipSize,
            // T-007-01: -1 sentinel means infinite cap for THIS transfer (FR-003-5 hides the line).
            DownloadsLeft: transfer.MaxDownloads < 0 ? null : transfer.MaxDownloads - transfer.DownloadsCount,
            PasswordRequired: passwordRequired,
            OrgName: branding.OrgName,
            OrgLogoUrl: logoUrl,
            ScheduledSendAtUtc: transfer.Status == wa.domain.TransferStatus.Scheduled ? transfer.ScheduledSendAtUtc : null,
            AdsEnabled: adsEnabled);
    }

    /// <summary>8 chars from the LinkId charset (TA-9.6) — anything else is treated as unknown on purpose.</summary>
    private static bool IsWellFormedLinkId(string linkId) =>
        linkId.Length == LinkId.Length && Enumerable.All(linkId, c => LinkId.Charset.Contains(c));
}
