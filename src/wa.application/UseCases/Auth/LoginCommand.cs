using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/login request (TA-4.2#9). Body: { email, password }.</summary>
/// <param name="Email">The email of the login command.</param>
/// <param name="Password">The password of the login command.</param>
public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;
