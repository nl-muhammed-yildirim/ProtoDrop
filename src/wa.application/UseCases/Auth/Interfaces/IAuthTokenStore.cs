using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (TA-9.1): the only DB surface the magic-link use cases need — read/write of AuthToken rows.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered as singleton over the adapter-owned WaDbContext.
/// </summary>
public interface IAuthTokenStore
{
    /// <summary>Look up a magic-link token row by its SHA-256 hex (64 chars) hash — single-use lookup on redeem.</summary>
    /// <param name="sha256Hex">Lowercase SHA-256 hex hash of the raw token.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The token row, or <see langword="null"/> when no token has that hash.</returns>
    Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct);

    /// <summary>Persist a new AuthToken row (hashed token + purpose + TTL).</summary>
    /// <param name="token">The new token row (hash, email, purpose and expiry already set).</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CreateAsync(AuthToken token, CancellationToken ct);

    /// <summary>Mark the row redeemed (first redeem wins — sets RedeemedAtUtc; single-use).</summary>
    /// <param name="id">Id of the token row to mark as redeemed.</param>
    /// <param name="redeemedAtUtc">UTC instant stored as the redemption time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct);
}
