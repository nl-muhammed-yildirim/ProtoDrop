using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter for ISenderBrandingStore (US-021-01): AppUser joined to Plan, non-deleted users only, never tracked.</summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class SenderBrandingStore(IDbContextFactory<WaDbContext> factory) : ISenderBrandingStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == ownerAppUserId && u.DeletedAtUtc == null)
            .Join(db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id,
                (u, p) => new SenderBrandingRow(p.Code, u.OrgName, u.OrgLogoBlobPath))
            .FirstOrDefaultAsync(ct);
    }
}
