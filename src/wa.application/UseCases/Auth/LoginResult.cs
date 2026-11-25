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
/// <param name="UserId">Primary key of the signed-in user.</param>
/// <param name="Email">Sign-in email; PII (TA-9.4).</param>
/// <param name="DisplayName">Name shown to recipients as the sender.</param>
/// <param name="Theme">Stored UI theme preference.</param>
/// <param name="PlanCode">Code of the user's current plan.</param>
/// <param name="SessionToken">Signed 30-day session token; the endpoint also sets it as the session cookie.</param>
public sealed record LoginResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
