using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (TA-9.1): the only DB surface the magic-link use cases need — read/write of AuthToken rows.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered as singleton over the adapter-owned WaDbContext.
/// </summary>
public interface IAuthTokenStore
{
    /// <summary>Look up a magic-link token row by its SHA-256 hex (64 chars) hash — single-use lookup on redeem.</summary>
    Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct);

    /// <summary>Persist a new AuthToken row (hashed token + purpose + TTL).</summary>
    Task CreateAsync(AuthToken token, CancellationToken ct);

    /// <summary>Mark the row redeemed (first redeem wins — sets RedeemedAtUtc; single-use).</summary>
    Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct);
}
