using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/signup request (TA-4.2#8). Body: { email, password }.</summary>
/// <param name="Email">The email of the signup command.</param>
/// <param name="Password">The password of the signup command.</param>
public sealed record SignupCommand(string Email, string Password) : IRequest<SignupResult>;
