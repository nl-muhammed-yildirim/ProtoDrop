using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-01 (TA-4.2a / EC-008-1): the AppUser surface signup needs — keep EF out of the application layer.
/// Implemented by a thin singleton adapter that opens a fresh WaDbContext per call (same style as IPublicTransferStore).
/// </summary>
public interface IAppUserStore
{
    /// <summary>Return the row matching the lowercased + trimmed email, or null when unknown (EC-008-4: sign-up is sign-in).</summary>
    /// <param name="lowercasedEmail">Email address, already trimmed and lowercased.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The app user, or <see langword="null"/> when there is none.</returns>
    Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct);

    /// <summary>Insert the new AppUser row — caller owns all field values (email already lowercased/trimmed).</summary>
    /// <param name="user">Fully populated new user; the store assigns nothing.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CreateAsync(AppUser user, CancellationToken ct);

    // T-008-06 (D-1): endpoint 13's three new store members.

    /// <summary>T-008-06: return the row for the session user id, or null when unknown OR soft-deleted (DeletedAtUtc != null) — endpoint 13 maps that to 404 NOT_FOUND.</summary>
    /// <param name="userId">Id of the user to read; soft-deleted users count as unknown.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The app user, or <see langword="null"/> when there is none.</returns>
    Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>T-008-06 (FR-008-4): partial profile update. null argument = leave the column alone; displayName is trimmed, and an empty result falls back to the email local part (US-008-04 alternative flow — never "From: (blank)").</summary>
    /// <param name="userId">Id of the user whose profile changes.</param>
    /// <param name="displayName">New display name, or null to keep the current one.</param>
    /// <param name="theme">New theme preference, or null to keep the current one.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct);

    /// <summary>T-008-06 / FR-008-8: soft-delete the AppUser row and re-home its transfers in ONE transaction — returns false when the user id is unknown.</summary>
    /// <param name="userId">Id of the user to delete.</param>
    /// <param name="deletedAtUtc">UTC instant stored as the deletion time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the user was deleted; <see langword="false"/> when the user id is unknown.</returns>
    Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct);

    /// <summary>
    /// CHORE-staging-blockers: a magic-link redeem proves ownership of the email, so mark it confirmed, discard the password set while it was unverified
    /// (an attacker may have pre-registered the address) and move the session epoch to <paramref name="nowUtc"/> so every session minted before this point dies, all in one UPDATE.
    /// A soft-deleted or unknown id is left alone.
    /// </summary>
    /// <param name="userId">Id of the user whose email was just proven.</param>
    /// <param name="nowUtc">The instant stored as the new session epoch.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when a live row was updated; <see langword="false"/> when the account is gone or soft-deleted.</returns>
    Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// TA-9.1: read the account's session epoch (<c>SessionsValidAfterUtc</c>) for the session middleware: one narrow keyed read per authenticated request.
    /// </summary>
    /// <param name="userId">Id of the session user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The epoch, or <see langword="null"/> when none was ever set or the id is unknown.</returns>
    Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct);
}
