using System.Text.Json.Serialization;
using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (TA-4.2#13): GET /api/v1/auth/me — the session user's profile DTO.</summary>
/// <param name="UserId">Identifier of the user of the get profile command.</param>
public sealed record GetProfileCommand(Guid UserId) : IRequest<GetProfileResult>;
