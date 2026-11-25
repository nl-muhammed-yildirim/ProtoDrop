using System.Security.Cryptography;
using MediatR;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/magic-link/redeem request (TA-4.2#9). Body: { token }.</summary>
/// <param name="Token">The raw base64 token from the magic-link URL.</param>
public sealed record RedeemMagicLinkCommand(string Token) : IRequest<RedeemMagicLinkResult>;
