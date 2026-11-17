using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): admin flag write surface — single-key PK upsert over WaDbContext.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AdminFlagStore : IAdminFlagStore
{
    private readonly WaDbContext _db;

    public AdminFlagStore(WaDbContext db) => _db = db;

    /// <summary>
    /// T-011-08 (TA-4.2#23): single-key PK upsert — read the old row by Key, then ExecuteUpdateAsync
    /// Value + UpdatedAtUtc WHERE Key==key (last-write-wins, EC-011-1). No transaction (single-row update).
    /// Returns the OLD row (pre-update) so the handler can audit {old,new} — or null when no row for that key.
    /// </summary>
    public async Task<FeatureFlag?> UpsertAsync(string key, string value, DateTime updatedAtUtc, CancellationToken ct)
    {
        // 1. Read the old row (AsNoTracking — no change tracking on this write path).
        var old = await _db.Set<FeatureFlag>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Key == key, ct);

        if (old is null)
            return null; // unknown key → handler throws ForNotFound (404 NOT_FOUND at the endpoint)

        // 2. Single-row update via ExecuteUpdateAsync — mirrors AdminUserStore.SetPlanIdAsync's idiom (L77-84).
        await _db.Set<FeatureFlag>()
            .Where(f => f.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.Value, value)
                .SetProperty(f => f.UpdatedAtUtc, updatedAtUtc), ct);

        return old; // the pre-update row — the handler audits {old,new} from it (FR-011-5)
    }
}
