using MediatR;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (D-B5): owner read — preview URL is minted whatever the plan (own data, EC-021-5).</summary>
public sealed class GetOrgLogoHandler : IRequestHandler<GetOrgLogoQuery, OrgLogoView>
{
    private readonly IAppUserStore _users;
    private readonly IPlanContextAccessor _plan;
    private readonly IOrgLogoSasMinter _minter;

    /// <summary>Initializes a new instance of the <see cref="GetOrgLogoHandler"/> class.</summary>
    /// <param name="users">Store used to read the user's logo path.</param>
    /// <param name="plan">Accessor for the request's plan limits and feature toggles.</param>
    /// <param name="minter">Mints the read URL for the logo blob.</param>
    public GetOrgLogoHandler(IAppUserStore users, IPlanContextAccessor plan, IOrgLogoSasMinter minter)
    {
        _users = users;
        _plan = plan;
        _minter = minter;
    }

    /// <inheritdoc />
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
