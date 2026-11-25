using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (D-B5): GET /api/v1/auth/me/logo.</summary>
/// <param name="UserId">Identifier of the user of the get org logo query.</param>
public sealed record GetOrgLogoQuery(Guid UserId) : IRequest<OrgLogoView>;
