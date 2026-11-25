using MediatR;
using Microsoft.AspNetCore.Identity;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// POST /api/v1/auth/login result — the profile DTO plus the TA-9.1 session-token value.
/// Same shape as SignupResult; the wire shape is pinned at the endpoint's single construction site (TA-4.2a).
/// </summary>
/// <param name="UserId">Identifier of the user of the login result.</param>
/// <param name="Email">The email of the login result.</param>
/// <param name="DisplayName">The display name of the login result.</param>
/// <param name="Theme">The theme of the login result.</param>
/// <param name="PlanCode">The plan code of the login result.</param>
/// <param name="SessionToken">The session token of the login result.</param>
public sealed record LoginResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
