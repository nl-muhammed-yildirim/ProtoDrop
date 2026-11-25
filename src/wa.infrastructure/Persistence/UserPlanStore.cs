using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter for IUserPlanStore (US-020-01): AppUser → Plan code over the adapter-owned WaDbContext.</summary>
/// <param name="db">Database context that backs the store.</param>
public sealed class UserPlanStore(WaDbContext db) : IUserPlanStore
{
    private readonly WaDbContext _db = db;

    /// <inheritdoc />
    /// <remarks>
    /// EC-020-3: deliberately NO DeletedAtUtc predicate — a soft-deleted user resolves to the last-known plan.
    /// AsNoTracking: the singleton context must not cache rows, so a plan change applies on the next request.
    /// </remarks>
    public async Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId)
            .Join(_db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id, (u, p) => p.Code)
            .FirstOrDefaultAsync(cancellationToken);
}
