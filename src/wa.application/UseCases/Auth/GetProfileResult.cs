using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

// Wire shape pinned at the endpoint's single construction site (TA-4.2a): identical to LoginResult minus SessionToken —
// the cookie already carries the session, /auth/me does not re-mint one.
public sealed record GetProfileResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string? OrgName = null,
    bool EmailNotifications = true); // F-XCT-003 (FR-046-2): effective value — the column's NULL→true rule at both construction sites
