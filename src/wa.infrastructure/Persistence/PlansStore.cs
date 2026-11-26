using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded Plan rows (TA-0.2 rule 7: the only place IPlansStore reads the DB).</summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class PlansStore(IDbContextFactory<WaDbContext> factory) : IPlansStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        // AsNoTracking: read-only query, no change tracking needed (FR-007-5).
        var row = await db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
        return row ?? throw new PlanUnknownException(planCode); // AC-053-4: loud, never null/empty.
    }

    /// <inheritdoc />
    public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken); // T-008-02: thin, AsNoTracking like existing methods.
    }
}
