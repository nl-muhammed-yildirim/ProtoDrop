using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-02 (FR-011-5 / TA-3.2): thin adapter over WaDbContext implementing IAuditStore — one bounded insert per call.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class AuditStore(IDbContextFactory<WaDbContext> factory) : IAuditStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>One Add + one SaveChanges — no extra round-trips (AC-009-1 idiom).</remarks>
    public async Task AddAsync(AuditLog row, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<AuditLog>().Add(row);
        await db.SaveChangesAsync(ct);
    }
}
