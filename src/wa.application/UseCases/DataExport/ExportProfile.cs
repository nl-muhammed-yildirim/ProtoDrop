namespace wa.application.UseCases.DataExport;

/// <summary>profile.json source (FR-047-1).</summary>
public sealed record ExportProfile(string Email, string DisplayName, string Theme, string PlanCode, DateTime CreatedAtUtc, string? OrgName = null, bool EmailNotifications = true); // F-XCT-003 (FR-046-1): effective column value — NULL → true
