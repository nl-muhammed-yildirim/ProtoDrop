using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo.</summary>
/// <param name="UserId">Identifier of the user of the remove org logo command.</param>
public sealed record RemoveOrgLogoCommand(Guid UserId) : IRequest<Unit>;
