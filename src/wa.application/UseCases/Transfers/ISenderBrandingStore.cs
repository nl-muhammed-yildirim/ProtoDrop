namespace wa.application.UseCases.Transfers;

/// <summary>A transfer owner's plan code plus profile branding values (F-PRF-001).</summary>
public sealed record SenderBrandingRow(string PlanCode, string? OrgName, string? OrgLogoBlobPath);

/// <summary>Read port for the sender's branding (F-PRF-001 US-021-01). Returns null for an unknown or soft-deleted user.</summary>
public interface ISenderBrandingStore
{
    Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct);
}
