using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter for IUserPlanStore (US-020-01): AppUser → Plan code over the adapter-owned WaDbContext.</summary>
public sealed class UserPlanStore : IUserPlanStore
{
    private readonly WaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="UserPlanStore"/> class.</summary>
    /// <param name="db">The database.</param>
    public UserPlanStore(WaDbContext db) => _db = db;

    /// <summary>
    /// EC-020-3: deliberately NO DeletedAtUtc predicate — a soft-deleted user resolves to the last-known plan.
    /// AsNoTracking: the singleton context must not cache rows, so a plan change applies on the next request.
    /// </summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The resulting text.</returns>
    public async Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId)
            .Join(_db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id, (u, p) => p.Code)
            .FirstOrDefaultAsync(cancellationToken);
}
