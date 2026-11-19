using MediatR;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo.</summary>
public sealed record RemoveOrgLogoCommand(Guid UserId) : IRequest<Unit>;

/// <summary>
/// Deletes the blob first, then clears OrgLogoBlobPath: a failed blob delete surfaces with the path still set, so the
/// owner can retry and no orphan is left unreachable. Idempotent (no stored path = no-op). Not plan-gated: an owner may always
/// remove their own data.
/// </summary>
public sealed class RemoveOrgLogoHandler : IRequestHandler<RemoveOrgLogoCommand, Unit>
{
    private readonly IAppUserStore _users;
    private readonly IOrgLogoStore _logos;
    private readonly IBlobStore _blobs;

    public RemoveOrgLogoHandler(IAppUserStore users, IOrgLogoStore logos, IBlobStore blobs)
    {
        _users = users;
        _logos = logos;
        _blobs = blobs;
    }

    public async Task<Unit> Handle(RemoveOrgLogoCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var path = user.OrgLogoBlobPath;
        if (string.IsNullOrEmpty(path))
            return Unit.Value;

        await _blobs.DeleteAsync(path, cancellationToken);
        await _logos.SetPathAsync(request.UserId, null, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>US-021-04: the owner's logo state — preview URL (30-min inline SAS, null when none), the plan's MAX_LOGO_SIZE and whether branding is on for the plan.</summary>
public sealed record OrgLogoView(Uri? LogoUrl, long MaxLogoSize, bool BrandingEnabled);

/// <summary>US-021-04 (FR-021-3): PUT /api/v1/auth/me/logo — the raw request body is the image. Not disposed by the handler.</summary>
public sealed record UploadOrgLogoCommand(Guid UserId, Stream Content) : IRequest<OrgLogoView>;

/// <summary>US-021-04 (D-B5): GET /api/v1/auth/me/logo.</summary>
public sealed record GetOrgLogoQuery(Guid UserId) : IRequest<OrgLogoView>;

/// <summary>A logo upload rejected before anything was stored; <see cref="Code"/> is a closed ProblemCodes member.</summary>
public sealed class OrgLogoRejectedException : Exception
{
    public string Code { get; }

    private OrgLogoRejectedException(string code, string message) : base(message) => Code = code;

    /// <summary>FR-021-4: the sender's plan has no branding → 403 FORBIDDEN.</summary>
    public static OrgLogoRejectedException ForPlan() =>
        new(ProblemCodes.Forbidden, "Pro plan required.");

    /// <summary>AC-021-4: body larger than MAX_LOGO_SIZE → 422 LOGO_TOO_LARGE; the message names the limit.</summary>
    public static OrgLogoRejectedException ForTooLarge(long limit) =>
        new(ProblemCodes.LogoTooLarge, $"Logo exceeds the {FormatMegabytes(limit)} limit ({limit} bytes).");

    /// <summary>FR-021-5: content is not PNG/JPEG/WebP/SVG → 415 LOGO_TYPE_INVALID.</summary>
    public static OrgLogoRejectedException ForType() =>
        new(ProblemCodes.LogoTypeInvalid, "Logo must be a PNG, JPEG, WebP or SVG image.");

    private static string FormatMegabytes(long bytes) =>
        $"{bytes / (1024.0 * 1024.0):0.##} MB";
}

/// <summary>
/// US-021-04 (FR-021-3..5, D-B1): validates, stores and records the org logo. Order: user (404) → plan branding (403) →
/// bounded read, size (422) → content sniff (415) → blob write → path update → delete the superseded blob. Every rejection
/// happens before any write; a failed path update removes the just-written blob unless it replaced the canonical one.
/// </summary>
public sealed class UploadOrgLogoHandler : IRequestHandler<UploadOrgLogoCommand, OrgLogoView>
{
    private const int ReadChunk = 81920;
    private const int SniffBytes = 1024;

    private readonly IAppUserStore _users;
    private readonly IPlanContextAccessor _plan;
    private readonly IBlobContentWriter _writer;
    private readonly IBlobStore _blobs;
    private readonly IOrgLogoStore _logos;
    private readonly IOrgLogoSasMinter _minter;

    public UploadOrgLogoHandler(
        IAppUserStore users, IPlanContextAccessor plan, IBlobContentWriter writer,
        IBlobStore blobs, IOrgLogoStore logos, IOrgLogoSasMinter minter)
    {
        _users = users;
        _plan = plan;
        _writer = writer;
        _blobs = blobs;
        _logos = logos;
        _minter = minter;
    }

    public async Task<OrgLogoView> Handle(UploadOrgLogoCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var ctx = await _plan.GetAsync(cancellationToken);
        if (!ctx.Features.Branding)
            throw OrgLogoRejectedException.ForPlan();

        var limit = ctx.Limits.MaxLogoSize;
        var (buffer, length) = await ReadBoundedAsync(request.Content, limit, cancellationToken);
        if (length > limit)
            throw OrgLogoRejectedException.ForTooLarge(limit);

        var type = LogoContentSniffer.Sniff(buffer.AsSpan(0, Math.Min(length, SniffBytes)));
        if (type is null)
            throw OrgLogoRejectedException.ForType();

        var canonical = BlobPaths.OrgLogo(request.UserId);
        var previous = user.OrgLogoBlobPath;

        using (var content = new MemoryStream(buffer, 0, length, writable: false))
            await _writer.UploadAsync(canonical, content, LogoContentSniffer.ContentTypeOf(type.Value), cancellationToken);

        try
        {
            // false = no live row matched (soft-deleted between the read above and now): same cleanup as a failed write.
            if (!await _logos.SetPathAsync(request.UserId, canonical, cancellationToken))
                throw MeProfileException.ForNotFound();
        }
        catch
        {
            // No partial state: when the blob was freshly created (no canonical blob before), remove it. If the previous
            // path already was the canonical one, the column is unchanged and the new bytes are simply the live logo.
            if (!string.Equals(previous, canonical, StringComparison.Ordinal))
                await _blobs.DeleteAsync(canonical, CancellationToken.None);
            throw;
        }

        if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, canonical, StringComparison.Ordinal))
            await _blobs.DeleteAsync(previous, cancellationToken);

        return new OrgLogoView(_minter.MintOrgLogo(request.UserId), limit, true);
    }

    /// <summary>Reads at most limit + 1 bytes: an oversize body is detected without buffering it.</summary>
    private static async Task<(byte[] Buffer, int Length)> ReadBoundedAsync(Stream source, long limit, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var chunk = new byte[ReadChunk];
        while (ms.Length <= limit)
        {
            var want = (int)Math.Min(chunk.Length, limit + 1 - ms.Length);
            var read = await source.ReadAsync(chunk.AsMemory(0, want), ct);
            if (read == 0)
                break;
            ms.Write(chunk, 0, read);
        }

        return (ms.GetBuffer(), (int)ms.Length);
    }
}

/// <summary>US-021-04 (D-B5): owner read — preview URL is minted whatever the plan (own data, EC-021-5).</summary>
public sealed class GetOrgLogoHandler : IRequestHandler<GetOrgLogoQuery, OrgLogoView>
{
    private readonly IAppUserStore _users;
    private readonly IPlanContextAccessor _plan;
    private readonly IOrgLogoSasMinter _minter;

    public GetOrgLogoHandler(IAppUserStore users, IPlanContextAccessor plan, IOrgLogoSasMinter minter)
    {
        _users = users;
        _plan = plan;
        _minter = minter;
    }

    public async Task<OrgLogoView> Handle(GetOrgLogoQuery request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var ctx = await _plan.GetAsync(cancellationToken);
        var url = string.IsNullOrEmpty(user.OrgLogoBlobPath) ? null : _minter.MintOrgLogo(request.UserId);
        return new OrgLogoView(url, ctx.Limits.MaxLogoSize, ctx.Features.Branding);
    }
}
