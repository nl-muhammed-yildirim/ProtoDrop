using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/signup request (TA-4.2#8). Body: { email, password }.</summary>
/// <param name="Email">Address to sign up or sign in with; PII (TA-9.4).</param>
/// <param name="Password">Plain-text password of at least 8 characters; hashed before it is stored.</param>
public sealed record SignupCommand(string Email, string Password) : IRequest<SignupResult>;
