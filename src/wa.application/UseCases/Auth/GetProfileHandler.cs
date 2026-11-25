using System.Text.Json.Serialization;
using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (TA-4.2#13): GET /api/v1/auth/me — returns the profile DTO verbatim from the session user's row.</summary>
public sealed class GetProfileHandler : IRequestHandler<GetProfileCommand, GetProfileResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;

    /// <summary>Initializes a new instance of the <see cref="GetProfileHandler"/> class.</summary>
    /// <param name="users">Store used to read the user row.</param>
    /// <param name="plans">Store used to resolve the plan code of the user.</param>
    public GetProfileHandler(IAppUserStore users, IPlansStore plans)
    {
        _users = users;
        _plans = plans;
    }

    /// <inheritdoc />
    public async Task<GetProfileResult> Handle(GetProfileCommand request, CancellationToken cancellationToken)
    {
        // FR-008-4: no row (unknown OR soft-deleted) → the endpoint maps this to 404 NOT_FOUND (spec's literal pin).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // FR-008-4: plan code resolved via IPlansStore — mirrors LoginHandler exactly (null plan row → same not-found shape, no new code).
        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw MeProfileException.ForNotFound();

        return new GetProfileResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, user.OrgName, AppUser.EmailNotificationsOn(user.EmailNotificationsEnabled));
    }
}
