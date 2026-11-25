using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo.</summary>
/// <param name="UserId">Id of the signed-in user whose logo is removed.</param>
public sealed record RemoveOrgLogoCommand(Guid UserId) : IRequest<Unit>;
