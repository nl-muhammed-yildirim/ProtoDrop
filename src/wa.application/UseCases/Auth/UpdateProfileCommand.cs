using System.Text.Json.Serialization;
using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial update (null = leave the field alone).</summary>
/// <param name="UserId">Identifier of the user of the update profile command.</param>
/// <param name="DisplayName">The display name of the update profile command.</param>
/// <param name="Theme">The theme of the update profile command.</param>
/// <param name="OrgName">The org name of the update profile command.</param>
/// <param name="EmailNotifications">The email notifications of the update profile command.</param>
public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null) : IRequest<GetProfileResult>;
