using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;
using wa.domain.Transfers; // T-011-06: CursorCodec (TA-4.1.4 pin) for the nextCursor

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): admin users list — single bounded query over WaDbContext.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AdminUserStore : IAdminUserStore
{
    private readonly WaDbContext _db;

    public AdminUserStore(WaDbContext db) => _db = db;

    /// <summary>
    /// T-011-06 (TA-4.2#22): single bounded query mirroring SearchTransfersStore's shape — OrderByDescending(CreatedAtUtc).ThenByDescending(Id)
    /// + cursor predicate (CreatedAtUtc &lt; @afterUtc OR (= AND Id &lt; @afterIdExcluded)) + Take(limit+1) for next-page detection.
    /// q semantics (DECISION): plain Contains on the normalized term — AppUser.Email is stored lowercased so a lowercase q matches case-insensitively.
    /// Per-row computed columns via scalar subqueries (EF translates to correlated subselects — bounded per row at Take(25)):
    ///   PlanName            = scalar subquery over Plan (FirstOrDefault).
    ///   StorageBytesUsed    = T-010-04's 3-step expression inlined as a nested subquery (Status IN (1,3), per-ref counting US-010-03).
    ///   ActiveTransferCount = scalar subquery over Transfer (Status == 1 only — task file literal).
    /// </summary>
    public async Task<(List<AdminUserRow> Items, string? NextCursor)> GetUsersAsync(
        string? q,
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct)
    {
        var rows = await _db.Set<AppUser>()
            .AsNoTracking()
            // T-011-06 (DECISION): q predicate — email substring on the normalized term.
            .Where(u => q == null || u.Email.Contains(q))
            // TA-4.1.4: cursor predicate — exactly as ListByOwnerAsync L65 / SearchTransfersStore L50.
            .Where(u => afterUtc == null || u.CreatedAtUtc < afterUtc.Value || (u.CreatedAtUtc == afterUtc.Value && u.Id < afterIdExcluded!.Value))
            .OrderByDescending(u => u.CreatedAtUtc)
            .ThenByDescending(u => u.Id)
            .Take(limit + 1)
            .Select(u => new AdminUserRow(
                u.Id,
                u.Email,
                // Plan name via scalar subquery (FirstOrDefault) — Plans are seeded and never deleted; null handled.
                _db.Set<Plan>().Where(p => p.Id == u.PlanId).Select(p => p.Name).FirstOrDefault(),
                // T-010-04 (US-010-03): storage used — 3-step expression inlined as a nested subquery (Status IN (1,3), per-ref counting).
                _db.Set<BlobRef>()
                    .Where(b => _db.Set<FileItem>()
                        .Where(f => _db.Set<Transfer>()
                            .Where(t => t.OwnerAppUserId == u.Id && (t.Status == 1 || t.Status == 3))
                            .Select(t => t.Id)
                            .Contains(f.TransferId))
                        .Select(f => f.BlobRefId)
                        .Distinct()
                        .Contains(b.Id))
                    .Sum(b => b.SizeBytes),
                // Active transfer count — Status == 1 only (task file literal).
                _db.Set<Transfer>().Count(t => t.OwnerAppUserId == u.Id && t.Status == 1),
                u.CreatedAtUtc))
            .ToListAsync(ct);

        var items = rows.Take(limit).ToList();
        // TA-4.1.4: cursor = base64("{createdAtUtc:O}:{id:N}") of the last returned row — null when no next page.
        var nextCursor = rows.Count > limit
            ? CursorCodec.Encode(rows[limit - 1].CreatedAtUtc, rows[limit - 1].Id)
            : null;

        return (items, nextCursor);
    }

    /// <summary>
    /// T-011-07 (TA-4.2#22 / FR-011-2): set AppUser.PlanId via ExecuteUpdateAsync — mirrors AppUserStore.SoftDeleteWithReHomeAsync's
    /// idiom (L57-69). Single-row update, no transaction needed. Returns false when the row vanished in a race.
    /// </summary>
    public async Task<bool> SetPlanIdAsync(Guid userId, Guid newPlanId, CancellationToken ct)
    {
        var updatedUsers = await _db.Set<AppUser>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PlanId, newPlanId), ct);

        return updatedUsers > 0; // false only in the race where a concurrent force-delete already committed — handler maps to the same not-found shape
    }
}
