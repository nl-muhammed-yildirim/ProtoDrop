namespace wa.application.UseCases.Transfers;

/// <summary>Read port for the sender's branding (F-PRF-001 US-021-01). Returns null for an unknown or soft-deleted user.</summary>
public interface ISenderBrandingStore
{
    /// <summary>Reads the sender's organization name, logo path and plan code.</summary>
    /// <param name="ownerAppUserId">The sending user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The organization name, logo path and plan code of the sender, or <see langword="null"/> when the user is unknown or soft-deleted.</returns>
    Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct);
}
