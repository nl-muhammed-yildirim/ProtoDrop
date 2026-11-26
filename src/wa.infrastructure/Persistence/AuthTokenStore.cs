using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-008-04 (TA-9.1): thin adapter over WaDbContext implementing IAuthTokenStore — the only DB surface magic-link needs.
/// Singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime), like AppUserStore.
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class AuthTokenStore(IDbContextFactory<WaDbContext> factory) : IAuthTokenStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>TA-9.1: lookup by the SHA-256 hex token hash — single-use identity (UQ_AuthToken_Hash unique index).</remarks>
    public async Task<AuthToken?> FindByHashAsync(string sha256Hex, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AuthToken>().FirstOrDefaultAsync(t => t.TokenHash == sha256Hex, ct);
    }

    /// <inheritdoc />
    /// <remarks>Persist the new AuthToken row (hashed token + purpose + TTL).</remarks>
    public async Task CreateAsync(AuthToken token, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<AuthToken>().Add(token);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>Mark the row redeemed — first redeem wins (single-use). No-op when the row is already redeemed.</remarks>
    public async Task MarkRedeemedAsync(Guid id, DateTime redeemedAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var token = await db.Set<AuthToken>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null || token.RedeemedAtUtc is not null)
            return;

        token.RedeemedAtUtc = redeemedAtUtc;
        await db.SaveChangesAsync(ct);
    }
}
