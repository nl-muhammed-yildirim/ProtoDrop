using System.Security.Cryptography;
using MediatR;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/forgot-password request (TA-4.2#11). Body: { email }.</summary>
/// <param name="Email">The email of the forgot password command.</param>
public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResult>;
