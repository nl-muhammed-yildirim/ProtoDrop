using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded FeatureFlag rows (TA-0.2 rule 7: the only place IFlagsStore reads the DB).</summary>
/// <param name="db">Database context that backs the store.</param>
public sealed class FlagsStore(WaDbContext db) : IFlagsStore
{
    private readonly WaDbContext _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
        await _db.Set<FeatureFlag>().AsNoTracking().ToListAsync(cancellationToken);
}
