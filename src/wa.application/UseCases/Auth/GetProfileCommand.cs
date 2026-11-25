using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (TA-4.2#13): GET /api/v1/auth/me — the session user's profile DTO.</summary>
/// <param name="UserId">Id of the signed-in user whose profile is read.</param>
public sealed record GetProfileCommand(Guid UserId) : IRequest<GetProfileResult>;
