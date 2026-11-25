using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/forgot-password request (TA-4.2#11). Body: { email }.</summary>
/// <param name="Email">Address that asked for a password reset; PII (TA-9.4).</param>
public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResult>;
