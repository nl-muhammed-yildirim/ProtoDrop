using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3): PUT /api/v1/auth/me/logo — the raw request body is the image. Not disposed by the handler.</summary>
/// <param name="UserId">Id of the signed-in user who uploads the logo.</param>
/// <param name="Content">Request body stream holding the image bytes; the caller owns and disposes it.</param>
public sealed record UploadOrgLogoCommand(Guid UserId, Stream Content) : IRequest<OrgLogoView>;
