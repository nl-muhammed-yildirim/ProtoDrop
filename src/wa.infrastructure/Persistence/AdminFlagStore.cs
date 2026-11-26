using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): admin flag write surface — single-key PK upsert over WaDbContext.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class AdminFlagStore(IDbContextFactory<WaDbContext> factory) : IAdminFlagStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>
    /// T-011-08 (TA-4.2#23): single-key PK upsert — read the old row by Key, then ExecuteUpdateAsync
    /// Value + UpdatedAtUtc WHERE Key==key (last-write-wins, EC-011-1). No transaction (single-row update).
    /// Returns the OLD row (pre-update) so the handler can audit {old,new} — or null when no row for that key.
    /// </remarks>
    public async Task<FeatureFlag?> UpsertAsync(string key, string value, DateTime updatedAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // 1. Read the old row (AsNoTracking — no change tracking on this write path).
        var old = await db.Set<FeatureFlag>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Key == key, ct);

        if (old is null)
            return null; // unknown key → handler throws ForNotFound (404 NOT_FOUND at the endpoint)

        // 2. Single-row update via ExecuteUpdateAsync — mirrors AdminUserStore.SetPlanIdAsync's idiom (L77-84).
        await db.Set<FeatureFlag>()
            .Where(f => f.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Value, value)
                .SetProperty(f => f.UpdatedAtUtc, updatedAtUtc), ct);

        return old; // the pre-update row — the handler audits {old,new} from it (FR-011-5)
    }
}
