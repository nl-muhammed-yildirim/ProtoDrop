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
    public async Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
    {
        var rows = await _db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
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
