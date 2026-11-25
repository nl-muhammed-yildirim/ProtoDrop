using MediatR;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Auth;

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
