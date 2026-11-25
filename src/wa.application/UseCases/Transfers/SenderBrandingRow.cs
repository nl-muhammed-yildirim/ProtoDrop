namespace wa.application.UseCases.Transfers;

/// <summary>A transfer owner's plan code plus profile branding values (F-PRF-001).</summary>
/// <param name="PlanCode">The plan code of the sender branding row.</param>
/// <param name="OrgName">The org name of the sender branding row.</param>
/// <param name="OrgLogoBlobPath">The org logo blob path of the sender branding row.</param>
public sealed record SenderBrandingRow(string PlanCode, string? OrgName, string? OrgLogoBlobPath);
