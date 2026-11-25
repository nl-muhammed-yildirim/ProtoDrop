using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-3): PUT /api/v1/auth/me/logo — the raw request body is the image. Not disposed by the handler.</summary>
/// <param name="UserId">Identifier of the user of the upload org logo command.</param>
/// <param name="Content">The content of the upload org logo command.</param>
public sealed record UploadOrgLogoCommand(Guid UserId, Stream Content) : IRequest<OrgLogoView>;
