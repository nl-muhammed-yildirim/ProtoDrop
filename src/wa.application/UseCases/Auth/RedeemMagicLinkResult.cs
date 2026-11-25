using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (D-4): redeem result — same profile DTO shape as LoginResult + session token value.
/// The wire shape is pinned at the endpoint's single construction site (TA-4.2a thin-endpoint rule).
/// </summary>
/// <param name="UserId">Identifier of the user of the redeem magic link result.</param>
/// <param name="Email">The email of the redeem magic link result.</param>
/// <param name="DisplayName">The display name of the redeem magic link result.</param>
/// <param name="Theme">The theme of the redeem magic link result.</param>
/// <param name="PlanCode">The plan code of the redeem magic link result.</param>
/// <param name="SessionToken">The session token of the redeem magic link result.</param>
public sealed record RedeemMagicLinkResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
