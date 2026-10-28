using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

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
