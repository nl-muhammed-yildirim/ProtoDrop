using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Transfers; // T-009-01: CursorCodec (TA-4.1.4 pin) for ListByOwnerAsync's nextCursor

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-001-10: thin adapter over WaDbContext implementing ITransferStore — the only DB surface the finalize handler needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class TransferStore : ITransferStore
{
    private readonly WaDbContext _db;

    public TransferStore(WaDbContext db) => _db = db;

    public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        _db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == key, ct);

    public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) =>
        _db.Set<Transfer>().AnyAsync(t => t.LinkId == linkId, ct);

    public async Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) =>
        await _db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == ownerId && (t.Status == 1 || t.Status == 3))
            .SumAsync(t => t.TotalBytes, ct);

    public async Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
        await _db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == ownerId && (t.Status == 1 || t.Status == 3))
            .CountAsync(ct);

    /// <summary>
    /// T-009-01 (TA-4.1.4): owner-scoped My Files page. Single query — OrderByDescending(CreatedAtUtc) + cursor predicate
    /// (CreatedAtUtc &lt; @afterUtc OR (= AND Id &lt; @afterIdExcluded)) + Take(limit+1) for next-page detection; RecipientCount
    /// via scalar subquery over EmailRecipient (EF translates to a scalar subselect — no N+1); Name = first FileItem.OriginalName
    /// ordered by SortOrder then Id, or literal "Transfer" when the transfer has no files (no SelectMany per row).
    /// </summary>
    public async Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct)
    {
        var rows = await _db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
            // T-009-02 (US-009-02): status predicate only when a byte set is given — null = all.
            .Where(t => statuses == null || statuses.Contains(t.Status))
            .Where(t => afterUtc == null || t.CreatedAtUtc < afterUtc.Value || (t.CreatedAtUtc == afterUtc.Value && t.Id < afterIdExcluded!.Value))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Take(limit + 1)
            .Select(t => new
            {
                Id = t.Id,
                CreatedAtUtc = t.CreatedAtUtc,
                Name = _db.Set<FileItem>()
                    .Where(f => f.TransferId == t.Id)
                    .OrderBy(f => f.SortOrder)
                    .ThenBy(f => f.Id)
                    .Select(f => f.OriginalName)
                    .FirstOrDefault() ?? "Transfer",
                FileCount = t.FileCount,
                TotalBytes = t.TotalBytes,
                Status = t.Status,
                ExpiresAtUtc = t.ExpiresAtUtc,
                DownloadsCount = t.DownloadsCount,
                MaxDownloads = t.MaxDownloads,
                RecipientCount = _db.Set<EmailRecipient>().Count(r => r.TransferId == t.Id)
            })
            .ToListAsync(ct);

        var items = rows.Take(limit)
            .Select(r => new MyTransferRow(r.Id, r.Name, r.FileCount, r.TotalBytes, r.Status, r.ExpiresAtUtc, r.DownloadsCount, r.MaxDownloads, r.RecipientCount))
            .ToList();
        // TA-4.1.4: cursor = base64("{createdAtUtc:O}:{id:N}") of the last returned row — null when no next page.
        var nextCursor = rows.Count > limit
            ? CursorCodec.Encode(rows[limit - 1].CreatedAtUtc, rows[limit - 1].Id)
            : null;

        return (items, nextCursor);
    }

    /// <summary>
    /// T-009-03 (TA-4.2#15): owner-scoped My Files detail — three bounded queries total (AC-009-1: no per-row round-trips).
    /// Row predicate Id=@id AND OwnerAppUserId=@ownerId AND DeletedAtUtc==null in ONE query → foreign owner / guest-created
    /// / deleted / unknown id are all indistinguishable nulls (no FORBIDDEN path; enumeration-safe). Separate projections for
    /// files + recipients (PublicTransferStore.GetByLinkIdAsync idiom — no navigation from Transfer; golden rule 8: no EF
    /// entities in use cases), AsNoTracking throughout.
    /// </summary>
    public async Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct)
    {
        var t = await _db.Set<Transfer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null, ct);
        if (t is null) return null;

        var files = await _db.Set<FileItem>()
            .AsNoTracking()
            .Where(f => f.TransferId == id)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .Select(f => new GetMyTransferFile(f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);

        // Row Name = first file's OriginalName by SortOrder/Id or "Transfer" — mirrors ListByOwnerAsync's row shape (FR-009-1).
        var name = files.FirstOrDefault()?.Name ?? "Transfer";

        var recipients = await _db.Set<EmailRecipient>()
            .AsNoTracking()
            .Where(r => r.TransferId == id)
            .OrderBy(r => r.Id)
            .Select(r => r.Address)
            .ToListAsync(ct);

        return new GetMyTransferResult(
            new MyTransferRow(t.Id, name, t.FileCount, t.TotalBytes, t.Status, t.ExpiresAtUtc, t.DownloadsCount, t.MaxDownloads, recipients.Count),
            files, recipients);
    }

    /// <summary>
    /// T-009-04 (TA-4.2#17 / FR-009-3): owner delete — soft-delete only + RefCount decrement (mirrors
    /// ExpiryDeletionStore.DeleteTransfersAsync's idiom for a SINGLE transfer, three bounded queries total).
    /// </summary>
    public async Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct)
    {
        // 1. Load the row with owner-scoped predicate (Id=@id AND OwnerAppUserId=@ownerId AND Status != 4).
        var t = await _db.Set<Transfer>()
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerAppUserId == ownerId && t.Status != 4, ct);
        if (t is null) return null; // foreign owner / guest / already-deleted / unknown → handler returns null → NOT_FOUND

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

            var nowPlusBuffer = deletedAtUtc.AddHours(24); // FR-009-3: same 24h buffer as expiry idiom (TA-3.1)

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

    public async Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Set<Transfer>().Add(transfer);
        foreach (var br in blobRefs)
            _db.Set<BlobRef>().Add(br);
        foreach (var fi in fileItems)
            _db.Set<FileItem>().Add(fi);

        // Singleton context (adapter-owned): the handler's own FindByIdempotencyKeyAsync may already
        // have tracked a row for this key (e.g. an expired key pre-seeded by T-002-04). Reuse that
        // instance instead of Add — a second tracked instance with the same PK ('Key') throws an EF
        // identity conflict, and a blind INSERT would hit PK_Idem at the DB level.
        var existing = _db.Set<IdempotencyKey>().Local.FirstOrDefault(k => k.Key == idempotencyKey.Key)
            ?? await _db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == idempotencyKey.Key, ct);
        if (existing is not null)
        {
            existing.ResultJson = idempotencyKey.ResultJson;
            existing.TransferId = idempotencyKey.TransferId;
            existing.StatusCode = idempotencyKey.StatusCode;
            existing.ExpiresAtUtc = idempotencyKey.ExpiresAtUtc;
        }
        else
        {
            _db.Set<IdempotencyKey>().Add(idempotencyKey);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
