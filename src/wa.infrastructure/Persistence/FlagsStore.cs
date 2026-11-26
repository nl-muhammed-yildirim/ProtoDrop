using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded FeatureFlag rows (TA-0.2 rule 7: the only place IFlagsStore reads the DB).</summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class FlagsStore(IDbContextFactory<WaDbContext> factory) : IFlagsStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db.Set<FeatureFlag>().AsNoTracking().ToListAsync(cancellationToken);
    }
}
