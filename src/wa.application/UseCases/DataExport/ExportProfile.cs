namespace wa.application.UseCases.DataExport;

/// <summary>Source of profile.json in the export zip (FR-047-1).</summary>
/// <param name="Email">Account email; PII (TA-9.4), included because it is the user's own data.</param>
/// <param name="DisplayName">Name shown to recipients as the sender.</param>
/// <param name="Theme">Stored UI theme preference.</param>
/// <param name="PlanCode">Code of the user's current plan.</param>
/// <param name="CreatedAtUtc">UTC instant the account was created.</param>
/// <param name="OrgName">Organization name; null when unset.</param>
/// <param name="EmailNotifications">Effective owner-notification setting (null in storage counts as on).</param>
public sealed record ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc, string? OrgName = null, bool EmailNotifications = true); // F-XCT-003 (FR-046-1): effective column value — NULL → true
