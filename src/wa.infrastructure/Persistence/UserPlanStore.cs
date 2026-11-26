using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter for IUserPlanStore (US-020-01): AppUser → Plan code over a fresh WaDbContext per call.</summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class UserPlanStore(IDbContextFactory<WaDbContext> factory) : IUserPlanStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>
    /// EC-020-3: deliberately NO DeletedAtUtc predicate — a soft-deleted user resolves to the last-known plan.
    /// AsNoTracking: read-only query; every call uses a fresh context, so a plan change applies on the next request.
    /// </remarks>
    public async Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId)
            .Join(db.Set<Plan>().AsNoTracking(), u => u.PlanId, p => p.Id, (u, p) => p.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
