using System.Text.Json.Serialization;
using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial profile update. Only provided fields change;
/// null argument = leave the column alone (D-1 UpdateProfileAsync contract).
/// </summary>
public sealed class UpdateProfileHandler : IRequestHandler<UpdateProfileCommand, GetProfileResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IOrgNameStore _orgNames;
    private readonly IPlanContextAccessor _plan;
    private readonly IEmailNotificationsStore _emailNotifications;

    /// <summary>NVARCHAR(100) column width — TA-3.2 (verified in WaDbContextModelSnapshot). Single re-check site.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>The three closed theme values (spec + entity only — NOT in the frozen Open-Decisions-and-Constants registry).</summary>
    private static readonly string[] AllowedThemes = ["system", "light", "dark"];

    /// <summary>Initializes a new instance of the <see cref="UpdateProfileHandler"/> class.</summary>
    /// <param name="users">The users.</param>
    /// <param name="plans">The plans.</param>
    /// <param name="orgNames">The org names.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="emailNotifications">The email notifications.</param>
    public UpdateProfileHandler(IAppUserStore users, IPlansStore plans, IOrgNameStore orgNames, IPlanContextAccessor plan, IEmailNotificationsStore emailNotifications)
    {
        _users = users;
        _plans = plans;
        _orgNames = orgNames;
        _plan = plan;
        _emailNotifications = emailNotifications;
    }

    /// <inheritdoc />
    public async Task<GetProfileResult> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate FIRST: PATCH against a dead cookie is NOT "invalid theme" — it's not-found (404 at the endpoint).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // 2. Empty PATCH → VALIDATION (an all-null body changes nothing; reject it rather than waste a round-trip).
        if (request.DisplayName is null && request.Theme is null && !request.OrgName.IsSet && request.EmailNotifications is null)
            throw MeProfileException.ForEmptyPatch();

        // 3. theme validation (only when non-null): exactly system / light / dark → else VALIDATION.
        if (request.Theme is not null && !AllowedThemes.Contains(request.Theme))
            throw MeProfileException.ForInvalidTheme();

        // 4. displayName validation (only when non-null): after trim, length <= 100 → else VALIDATION.
        if (request.DisplayName is not null)
        {
            var trimmed = request.DisplayName.Trim();
            if (trimmed.Length > DisplayNameMaxLength)
                throw MeProfileException.ForDisplayNameTooLong(DisplayNameMaxLength.ToString());
        }

        // 4b. orgName validation (only when present in the body): no CR/LF, <= ORG_NAME_MAX_LENGTH after trim; blank = clear.
        string? orgName = null;
        if (request.OrgName.IsSet && request.OrgName.Value is { } rawOrg)
        {
            if (rawOrg.AsSpan().IndexOfAny('\r', '\n') >= 0)
                throw MeProfileException.ForInvalidOrgName();
            var trimmedOrg = rawOrg.Trim();
            if (trimmedOrg.Length > AppUser.OrgNameMaxLength)
                throw MeProfileException.ForInvalidOrgName();
            orgName = trimmedOrg.Length == 0 ? null : trimmedOrg;
        }

        // 4c. Setting a non-empty org name is a branding feature (same 403 as the logo upload); clearing is allowed on every plan.
        if (orgName is not null && !(await _plan.GetAsync(cancellationToken)).Features.Branding)
            throw OrgLogoRejectedException.ForPlan();

        // 5. Persist the partial update (D-1: null args are left alone; empty displayName falls back to the email local part).
        if (request.DisplayName is not null || request.Theme is not null)
            await _users.UpdateProfileAsync(request.UserId, request.DisplayName, request.Theme, cancellationToken);
        if (request.OrgName.IsSet)
            await _orgNames.SetOrgNameAsync(request.UserId, orgName, cancellationToken);

        // F-XCT-003 (FR-046-2): additive — absent leaves the column alone; the flag gate lives at the endpoint (FR-046-6).
        if (request.EmailNotifications is { } emailNotifications)
            await _emailNotifications.SetAsync(request.UserId, emailNotifications, cancellationToken);

        // PATCH response is byte-shaped like GET's — client can re-render without a follow-up request.
        return await ResolveResult(request.UserId, cancellationToken);
    }

    private async Task<GetProfileResult> ResolveResult(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw MeProfileException.ForNotFound();

        return new GetProfileResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, user.OrgName, AppUser.EmailNotificationsOn(user.EmailNotificationsEnabled));
    }
}
