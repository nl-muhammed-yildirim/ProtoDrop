namespace wa.application.UseCases.Transfers;

/// <summary>A transfer owner's plan code plus profile branding values (F-PRF-001).</summary>
public sealed record SenderBrandingRow(string PlanCode, string? OrgName, string? OrgLogoBlobPath);
