namespace wa.application.UseCases.Transfers;

/// <summary>Read port for the sender's branding (F-PRF-001 US-021-01). Returns null for an unknown or soft-deleted user.</summary>
public interface ISenderBrandingStore
{
    Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct);
}
