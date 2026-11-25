using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>EF adapter over seeded Plan rows (TA-0.2 rule 7: the only place IPlansStore reads the DB).</summary>
public sealed class PlansStore : IPlansStore
{
    private readonly WaDbContext _db;

    /// <summary>Initializes a new instance of the <see cref="PlansStore"/> class.</summary>
    /// <param name="db">Database context that backs the store.</param>
    public PlansStore(WaDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
    {
        // AsNoTracking: singleton WaDbContext identity map must not cache Plan rows (FR-007-5).
        var row = await _db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);
        return row ?? throw new PlanUnknownException(planCode); // AC-053-4: loud, never null/empty.
    }

    /// <inheritdoc />
    public async Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
        await _db.Set<Plan>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken); // T-008-02: thin, singleton, AsNoTracking like existing methods.
}
