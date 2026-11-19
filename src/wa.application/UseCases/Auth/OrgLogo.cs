using MediatR;
using wa.application.Ports;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo.</summary>
public sealed record RemoveOrgLogoCommand(Guid UserId) : IRequest<Unit>;

/// <summary>
/// Clears OrgLogoBlobPath first, then deletes the blob: the recipient page falls back to the org name at once, and a
/// failed blob delete only leaves an orphan. Idempotent (no stored path = no-op). Not plan-gated: an owner may always
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

        await _logos.SetPathAsync(request.UserId, null, cancellationToken);
        await _blobs.DeleteAsync(path, cancellationToken);
        return Unit.Value;
    }
}
