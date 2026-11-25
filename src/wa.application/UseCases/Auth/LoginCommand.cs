using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/login request (TA-4.2#9). Body: { email, password }.</summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;
