using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-02 (FR-011-5 / TA-3.2): thin adapter over WaDbContext implementing IAuditStore — one bounded insert per call.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AuditStore : IAuditStore
{
    private readonly WaDbContext _db;

    public AuditStore(WaDbContext db) => _db = db;

    /// <summary>One Add + one SaveChanges — no extra round-trips (AC-009-1 idiom).</summary>
    public async Task AddAsync(AuditLog row, CancellationToken ct)
    {
        _db.Set<AuditLog>().Add(row);
        await _db.SaveChangesAsync(ct);
    }
}
