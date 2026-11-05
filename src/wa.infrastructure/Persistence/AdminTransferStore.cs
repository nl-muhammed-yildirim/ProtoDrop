using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.application.UseCases.Transfers;
using wa.domain; // T-011-05: Transfer, FileItem, BlobRef entities (same as TransferStore.cs L3)

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — thin adapter over WaDbContext implementing IAdminTransferStore.
/// Mirrors TransferStore.MarkDeletedAsync at src/wa.infrastructure/Persistence/TransferStore.cs:143 with the owner predicate
/// removed (platform-scoped: Id=@id AND Status != 4). Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AdminTransferStore : IAdminTransferStore
{
    private readonly WaDbContext _db;

    public AdminTransferStore(WaDbContext db) => _db = db;

    /// <summary>
    /// T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — soft-delete only + RefCount decrement (mirrors
    /// TransferStore.MarkDeletedAsync with the owner predicate removed, three bounded queries total).
    /// </summary>
    public async Task<MarkDeleteResult?> AdminMarkDeletedAsync(Guid id, DateTime deletedAtUtc, CancellationToken ct)
    {
        // 1. Load the row with platform-scoped predicate (Id=@id AND Status != 4 — NO owner predicate).
        var t = await _db.Set<Transfer>()
            .FirstOrDefaultAsync(t => t.Id == id && t.Status != 4, ct);
        if (t is null) return null; // already-deleted / unknown → handler returns null → NOT_FOUND

        // 2. Soft-delete the row (TA-3.1: NO hard delete).
        t.Status = 4;
        t.DeletedAtUtc = deletedAtUtc;

        // 3. Decrement RefCount for each distinct BlobRefId referenced by this transfer's FileItems.
        var blobIds = await _db.Set<FileItem>()
            .AsNoTracking()
            .Where(f => f.TransferId == id)
            .Select(f => f.BlobRefId)
            .Distinct()
            .ToListAsync(ct);

        if (blobIds.Count > 0)
        {
            var blobRefs = await _db.Set<BlobRef>()
                .Where(b => blobIds.Contains(b.Id))
                .ToListAsync(ct);

            var nowPlusBuffer = deletedAtUtc.AddHours(24); // FR-011-4: same 24h buffer as expiry idiom (TA-3.1)

            foreach (var blob in blobRefs)
            {
                blob.RefCount -= 1;
                if (blob.RefCount <= 0)
                {
                    blob.RefCount = 0; // clamp to zero
                    blob.PhysicallyDeletedAtUtc = nowPlusBuffer;
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        return new MarkDeleteResult(deletedAtUtc, t.LinkId);
    }
}
