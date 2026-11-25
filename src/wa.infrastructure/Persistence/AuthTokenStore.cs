using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-008-04 (TA-9.1): thin adapter over WaDbContext implementing IAuthTokenStore — the only DB surface magic-link needs.
/// Singleton over the adapter-owned WaDbContext (same lifetime as AppUserStore, T-008-01).
/// </summary>
public sealed class AuthTokenStore : IAuthTokenStore
{
    private readonly WaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="AuthTokenStore"/> class.</summary>
    /// <param name="db">The database.</param>
    public AuthTokenStore(WaDbContext db) => _db = db;

    /// <summary>TA-9.1: lookup by the SHA-256 hex token hash — single-use identity (UQ_AuthToken_Hash unique index).</summary>
    /// <param name="sha256Hex">The sha256 hex.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The auth token, or <see langword="null"/> when there is none.</returns>
    public Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct) =>
        _db.Set<AuthToken>().FirstOrDefaultAsync(t => t.TokenHash == sha256Hex, ct);

    /// <summary>Persist the new AuthToken row (hashed token + purpose + TTL).</summary>
    /// <param name="token">The token.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    public async Task CreateAsync(AuthToken token, CancellationToken ct)
    {
        _db.Set<AuthToken>().Add(token);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Mark the row redeemed — first redeem wins (single-use). No-op when the row is already redeemed.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="redeemedAtUtc">The redeemed at UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    public async Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct)
    {
        var token = await _db.Set<AuthToken>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null || token.RedeemedAtUtc is not null)
            return;

        token.RedeemedAtUtc = redeemedAtUtc;
        await _db.SaveChangesAsync(ct);
    }
}
