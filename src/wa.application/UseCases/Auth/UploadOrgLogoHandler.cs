using MediatR;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Auth;

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

        bool updated;
        try
        {
            updated = await _logos.SetPathAsync(request.UserId, canonical, cancellationToken);
        }
        catch
        {
            // No partial state: when the blob was freshly created (no canonical blob before), remove it. If the previous
            // path already was the canonical one, the column is unchanged and the new bytes are simply the live logo.
            if (!string.Equals(previous, canonical, StringComparison.Ordinal))
                await _blobs.DeleteAsync(canonical, CancellationToken.None);
            throw;
        }

        if (!updated)
        {
            // No live row matched (soft-deleted between the read above and now): the canonical blob is orphaned whatever
            // the previous path was, so always remove it.
            await _blobs.DeleteAsync(canonical, CancellationToken.None);
            throw MeProfileException.ForNotFound();
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
