namespace wa.application.UseCases.Transfers;

/// <summary>Read port for the sender's branding (F-PRF-001 US-021-01). Returns null for an unknown or soft-deleted user.</summary>
public interface ISenderBrandingStore
{
    /// <summary>Gets the sender branding row.</summary>
    /// <param name="ownerAppUserId">Identifier of the owner app user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The sender branding row, or <see langword="null"/> when there is none.</returns>
    Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct);
}
