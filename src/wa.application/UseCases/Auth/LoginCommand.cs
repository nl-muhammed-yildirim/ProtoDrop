using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/login request (TA-4.2#9). Body: { email, password }.</summary>
/// <param name="Email">Sign-in email; PII (TA-9.4).</param>
/// <param name="Password">Plain-text password; verified against the stored hash and never logged.</param>
public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;
