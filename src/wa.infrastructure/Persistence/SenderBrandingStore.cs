using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter for ISenderBrandingStore (US-021-01): AppUser joined to Plan, non-deleted users only, never tracked.</summary>
public sealed class SenderBrandingStore : ISenderBrandingStore
{
    private readonly WaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="SenderBrandingStore"/> class.</summary>
    /// <param name="db">Database context that backs the store.</param>
    public SenderBrandingStore(WaDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct) =>
        await _db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == ownerAppUserId && u.DeletedAtUtc == null)
            .Join(_db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id,
                (u, p) => new SenderBrandingRow(p.Code, u.OrgName, u.OrgLogoBlobPath))
            .FirstOrDefaultAsync(ct);
}
