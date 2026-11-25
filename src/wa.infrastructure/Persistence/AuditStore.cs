using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-02 (FR-011-5 / TA-3.2): thin adapter over WaDbContext implementing IAuditStore — one bounded insert per call.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
/// <param name="db">Database context that backs the store.</param>
public sealed class AuditStore(WaDbContext db) : IAuditStore
{
    private readonly WaDbContext _db = db;

    /// <inheritdoc />
    /// <remarks>One Add + one SaveChanges — no extra round-trips (AC-009-1 idiom).</remarks>
    public async Task AddAsync(AuditLog row, CancellationToken ct)
    {
        _db.Set<AuditLog>().Add(row);
        await _db.SaveChangesAsync(ct);
    }
}
