using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (TA-9.1): the only DB surface the magic-link use cases need — read/write of AuthToken rows.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered as singleton over the adapter-owned WaDbContext.
/// </summary>
public interface IAuthTokenStore
{
    /// <summary>Look up a magic-link token row by its SHA-256 hex (64 chars) hash — single-use lookup on redeem.</summary>
    /// <param name="sha256Hex">The sha256 hex.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The auth token, or <see langword="null"/> when there is none.</returns>
    Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct);

    /// <summary>Persist a new AuthToken row (hashed token + purpose + TTL).</summary>
    /// <param name="token">The token.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task CreateAsync(AuthToken token, CancellationToken ct);

    /// <summary>Mark the row redeemed (first redeem wins — sets RedeemedAtUtc; single-use).</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="redeemedAtUtc">The redeemed at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct);
}
