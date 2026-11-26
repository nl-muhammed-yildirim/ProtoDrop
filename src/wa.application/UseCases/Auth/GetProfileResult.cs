namespace wa.application.UseCases.Auth;

/// <summary>
/// Wire shape pinned at the endpoint's single construction site (TA-4.2a): identical to LoginResult minus
/// SessionToken — the cookie already carries the session, /auth/me does not re-mint one.
/// </summary>
/// <param name="UserId">Primary key of the user.</param>
/// <param name="Email">Sign-in email; PII (TA-9.4).</param>
/// <param name="DisplayName">Name shown to recipients as the sender.</param>
/// <param name="Theme">Stored UI theme preference.</param>
/// <param name="PlanCode">Code of the user's current plan.</param>
/// <param name="OrgName">Organization name shown beside the sender name; null when unset.</param>
/// <param name="EmailConfirmed">Whether the account's email is confirmed; an unconfirmed account never gets an admin role.</param>
/// <param name="EmailNotifications">Effective owner-notification setting; true when the user never changed it.</param>
public sealed record GetProfileResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string? OrgName = null,
    bool EmailNotifications = true,
    bool EmailConfirmed = false); // F-XCT-003 (FR-046-2): effective value — the column's NULL→true rule at both construction sites
