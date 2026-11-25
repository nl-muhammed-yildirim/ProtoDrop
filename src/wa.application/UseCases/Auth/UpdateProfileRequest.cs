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

/// <summary>T-008-06: wire record bound from the PATCH JSON body by Minimal API (TA-4.2a). The session supplies UserId — NOT in the body;
/// constructed at Program.cs's single construction site as new UpdateProfileCommand(userId.Value, req.DisplayName, req.Theme, req.OrgName, req.EmailNotifications).</summary>
/// <param name="DisplayName">The display name of the update profile request.</param>
/// <param name="Theme">The theme of the update profile request.</param>
/// <param name="OrgName">The org name of the update profile request.</param>
/// <param name="EmailNotifications">The email notifications of the update profile request.</param>
public sealed record UpdateProfileRequest(string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null);
