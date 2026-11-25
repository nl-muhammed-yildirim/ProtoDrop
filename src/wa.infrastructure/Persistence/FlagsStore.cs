using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded FeatureFlag rows (TA-0.2 rule 7: the only place IFlagsStore reads the DB).</summary>
public sealed class FlagsStore : IFlagsStore
{
    private readonly WaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="FlagsStore"/> class.</summary>
    /// <param name="db">Database context that backs the store.</param>
    public FlagsStore(WaDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
        await _db.Set<FeatureFlag>().AsNoTracking().ToListAsync(cancellationToken);
}
