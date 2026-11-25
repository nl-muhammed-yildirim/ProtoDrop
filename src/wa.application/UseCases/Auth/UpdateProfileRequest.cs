namespace wa.application.UseCases.Auth;

/// <summary>T-008-06: wire record bound from the PATCH JSON body by Minimal API (TA-4.2a). The session supplies UserId — NOT in the body;
/// constructed at Program.cs's single construction site as new UpdateProfileCommand(userId.Value, req.DisplayName, req.Theme, req.OrgName, req.EmailNotifications).</summary>
/// <param name="DisplayName">New display name, or null to leave it unchanged.</param>
/// <param name="Theme">New theme (<c>system</c>, <c>light</c> or <c>dark</c>), or null to leave it unchanged.</param>
/// <param name="OrgName">Tri-state organization name patch: absent, cleared or set.</param>
/// <param name="EmailNotifications">New owner-notification setting, or null to leave it unchanged.</param>
public sealed record UpdateProfileRequest(string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null);
