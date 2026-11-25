namespace wa.application.UseCases.DataExport;

/// <summary>profile.json source (FR-047-1).</summary>
/// <param name="Email">The email of the export profile.</param>
/// <param name="DisplayName">The display name of the export profile.</param>
/// <param name="Theme">The theme of the export profile.</param>
/// <param name="PlanCode">The plan code of the export profile.</param>
/// <param name="CreatedAtUtc">The created at UTC of the export profile.</param>
/// <param name="OrgName">The org name of the export profile.</param>
/// <param name="EmailNotifications">Whether email notifications of the export profile.</param>
public sealed record ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc, string? OrgName = null, bool EmailNotifications = true); // F-XCT-003 (FR-046-1): effective column value — NULL → true
