using System.Security.Cryptography;
using MediatR;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/magic-link request (TA-4.2#10). Body: { email }.</summary>
/// <param name="Email">Address the sign-in link is sent to; PII (TA-9.4).</param>
public sealed record RequestMagicLinkCommand(string Email) : IRequest<RequestMagicLinkResult>;
