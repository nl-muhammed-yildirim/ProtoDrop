using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-09 (TA-4.2#24 / FR-011-2): admin suppressions adapter over WaDbContext (mirrors AdminFlagStore).
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AdminSuppressionStore : IAdminSuppressionStore
{
    private readonly WaDbContext _db;

    public AdminSuppressionStore(WaDbContext db) => _db = db;

    /// <summary>T-011-09 (TA-4.2#24): list all rows — no order contract (the handler orders).</summary>
    public async Task<IReadOnlyList<EmailSuppression>> ListAsync(CancellationToken ct)
    {
        return await _db.Set<EmailSuppression>()
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    /// T-011-09 (TA-4.2#24): single-row delete — returns the REMOVED row or null when no row for that Id.
    /// No transaction (single-row delete).
    /// </summary>
    public async Task<EmailSuppression?> RemoveByIdAsync(Guid id, CancellationToken ct)
    {
        var row = await _db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
            return null; // unknown id → handler passes through → endpoint 404 NOT_FOUND

        _db.Set<EmailSuppression>().Remove(row);
        await _db.SaveChangesAsync(ct);
        return row; // the removed row — the endpoint audits { email } from it (FR-011-5)
    }
}
