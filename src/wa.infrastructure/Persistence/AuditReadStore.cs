using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): read adapter over WaDbContext for AuditLog rows.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AuditReadStore : IAuditReadStore
{
    private readonly WaDbContext _db;

    public AuditReadStore(WaDbContext db) => _db = db;

    /// <summary>
    /// T-011-09 (TA-4.2#25): last count rows, newest first — Id is BIGINT IDENTITY so descending Id = newest first
    /// (no CreatedAtUtc tiebreak needed).
    /// </summary>
    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count, CancellationToken ct)
    {
        return await _db.Set<AuditLog>()
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(count)
            .ToListAsync(ct);
    }
}
