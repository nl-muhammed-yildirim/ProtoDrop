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

    public async Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Set<Transfer>().Add(transfer);
        foreach (var br in blobRefs)
            _db.Set<BlobRef>().Add(br);
        foreach (var fi in fileItems)
            _db.Set<FileItem>().Add(fi);
        _db.Set<IdempotencyKey>().Add(idempotencyKey);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
