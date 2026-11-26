using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): read adapter over WaDbContext for AuditLog rows.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class AuditReadStore(IDbContextFactory<WaDbContext> factory) : IAuditReadStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>
    /// T-011-09 (TA-4.2#25): last count rows, newest first — Id is BIGINT IDENTITY so descending Id = newest first
    /// (no CreatedAtUtc tiebreak needed).
    /// </remarks>
    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AuditLog>()
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(count)
            .ToListAsync(ct);
    }
}
