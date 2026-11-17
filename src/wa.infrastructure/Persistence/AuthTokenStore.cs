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

    public AuthTokenStore(WaDbContext db) => _db = db;

    /// <summary>TA-9.1: lookup by the SHA-256 hex token hash — single-use identity (UQ_AuthToken_Hash unique index).</summary>
    public Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct) =>
        _db.Set<AuthToken>().FirstOrDefaultAsync(t => t.TokenHash == sha256Hex, ct);

    /// <summary>Persist the new AuthToken row (hashed token + purpose + TTL).</summary>
    public async Task CreateAsync(AuthToken token, CancellationToken ct)
    {
        _db.Set<AuthToken>().Add(token);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Mark the row redeemed — first redeem wins (single-use). No-op when the row is already redeemed.</summary>
    public async Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct)
    {
        var token = await _db.Set<AuthToken>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null || token.RedeemedAtUtc is not null)
            return;

        token.RedeemedAtUtc = redeemedAtUtc;
        await _db.SaveChangesAsync(ct);
    }
}
