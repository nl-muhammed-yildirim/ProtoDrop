using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial update (null = leave the field alone).</summary>
/// <param name="UserId">Id of the signed-in user whose profile changes (taken from the session, not the body).</param>
/// <param name="DisplayName">New display name, or null to leave it unchanged.</param>
/// <param name="Theme">New theme (<c>system</c>, <c>light</c> or <c>dark</c>), or null to leave it unchanged.</param>
/// <param name="OrgName">Tri-state organization name patch: absent, cleared or set.</param>
/// <param name="EmailNotifications">New owner-notification setting, or null to leave it unchanged.</param>
public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null) : IRequest<GetProfileResult>;
