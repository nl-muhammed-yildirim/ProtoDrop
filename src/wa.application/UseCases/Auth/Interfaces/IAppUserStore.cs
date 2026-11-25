using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-01 (TA-4.2a / EC-008-1): the AppUser surface signup needs — keep EF out of the application layer.
/// Implemented by a thin singleton adapter over the adapter-owned WaDbContext (same style as IPublicTransferStore).
/// </summary>
public interface IAppUserStore
{
    /// <summary>Return the row matching the lowercased + trimmed email, or null when unknown (EC-008-4: sign-up is sign-in).</summary>
    /// <param name="lowercasedEmail">The lowercased email.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The app user, or <see langword="null"/> when there is none.</returns>
    Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct);

    /// <summary>Insert the new AppUser row — caller owns all field values (email already lowercased/trimmed).</summary>
    /// <param name="user">The user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CreateAsync(AppUser user, CancellationToken ct);

    // T-008-06 (D-1): endpoint 13's three new store members.

    /// <summary>T-008-06: return the row for the session user id, or null when unknown OR soft-deleted (DeletedAtUtc != null) — endpoint 13 maps that to 404 NOT_FOUND.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The app user, or <see langword="null"/> when there is none.</returns>
    Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>T-008-06 (FR-008-4): partial profile update. null argument = leave the column alone; displayName is trimmed, and an empty result falls back to the email local part (US-008-04 alternative flow — never "From: (blank)").</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="theme">The theme.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct);

    /// <summary>T-008-06 / FR-008-8: soft-delete the AppUser row and re-home its transfers in ONE transaction — returns false when the user id is unknown.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="deletedAtUtc">The deleted at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct);
}
