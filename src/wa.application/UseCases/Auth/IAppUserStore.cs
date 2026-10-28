using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-01 (TA-4.2a / EC-008-1): the AppUser surface signup needs — keep EF out of the application layer.
/// Implemented by a thin singleton adapter over the adapter-owned WaDbContext (same style as IPublicTransferStore).
/// </summary>
public interface IAppUserStore
{
    /// <summary>Return the row matching the lowercased + trimmed email, or null when unknown (EC-008-4: sign-up is sign-in).</summary>
    Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct);

    /// <summary>Insert the new AppUser row — caller owns all field values (email already lowercased/trimmed).</summary>
    Task CreateAsync(AppUser user, CancellationToken ct);
}
