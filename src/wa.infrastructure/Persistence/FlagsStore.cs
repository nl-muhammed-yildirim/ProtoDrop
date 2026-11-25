using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

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
