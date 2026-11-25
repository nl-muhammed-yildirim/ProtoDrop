using System.Security.Cryptography;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// POST /api/v1/auth/signup result — the profile DTO plus the TA-9.1 session-token value.
/// The wire shape is pinned at the endpoint's single construction site (TA-4.2a thin-endpoint rule).
/// </summary>
/// <param name="UserId">Identifier of the user of the signup result.</param>
/// <param name="Email">The email of the signup result.</param>
/// <param name="DisplayName">The display name of the signup result.</param>
/// <param name="Theme">The theme of the signup result.</param>
/// <param name="PlanCode">The plan code of the signup result.</param>
/// <param name="SessionToken">The session token of the signup result.</param>
public sealed record SignupResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
