namespace wa.application.UseCases.Transfers;

/// <summary>A transfer owner's plan code plus profile branding values (F-PRF-001).</summary>
/// <param name="PlanCode">Code of the sender's current plan.</param>
/// <param name="OrgName">Stored organization name; null or blank when unset.</param>
/// <param name="OrgLogoBlobPath">Blob path of the stored logo; null when there is none.</param>
public sealed record SenderBrandingRow(string PlanCode, string? OrgName, string? OrgLogoBlobPath);
