using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded Plan rows (TA-0.2 rule 7: the only place IPlansStore reads the DB).</summary>
public sealed class PlansStore : IPlansStore
{
    private readonly WaDbContext _db;

    public PlansStore(WaDbContext db) => _db = db;

    public async Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
    {
        // AsNoTracking: singleton WaDbContext identity map must not cache Plan rows (FR-007-5).
        var row = await _db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
        return row ?? throw new PlanUnknownException(planCode); // AC-053-4: loud, never null/empty.
    }

    public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
        await _db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken); // T-008-02: thin, singleton, AsNoTracking like existing methods.
}

/// <summary>EF adapter over seeded FeatureFlag rows (TA-0.2 rule 7: the only place IFlagsStore reads the DB).</summary>
public sealed class FlagsStore : IFlagsStore
{
    private readonly WaDbContext _db;

    public FlagsStore(WaDbContext db) => _db = db;

    // AsNoTracking: singleton WaDbContext identity map froze flag overrides until restart;
    // FR-007-5 requires limits editable via flags without a deploy (EC-007-1).
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
        await _db.Set<FeatureFlag>().AsNoTracking().ToListAsync(cancellationToken);
}
