using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-002-04: thin adapter over WaDbContext implementing ISendStore — the only DB surface the send handler needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class SendStore : ISendStore
{
    private readonly WaDbContext _db;

    public SendStore(WaDbContext db) => _db = db;

    public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Set<Transfer>().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // T-010-01 (AC-010-1): a re-send draft carries SourcedFrom = original.Id — set SupersededBy on the original row
        // inside this same transaction. rowsAffected 0 → the original was deleted while the draft sat open — no error here;
        // EC-010-2's FILES_GONE gate is T-010-02's scope (it inserts its predicate before this write).
        if (transfer.SourcedFrom is not null)
        {
            _ = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE [Transfer] SET [SupersededBy] = {0} WHERE [Id] = {1}",
                [transfer.Id, transfer.SourcedFrom.Value], ct);

            // The raw-SQL UPDATE bypasses the change tracker: if this original row is tracked in the singleton context
            // (it was loaded by the earlier resend command), detach it so a later SaveChanges can't resurrect the old SupersededBy
            // (same convention as PublicTransferStore.RecordSingleFileDownloadAsync).
            var tracked = _db.Set<Transfer>().Local.FirstOrDefault(t => t.Id == transfer.SourcedFrom.Value);
            if (tracked is not null)
                _db.Set<Transfer>().Local.Remove(tracked);
        }

        // T-010-01: re-send drafts materialized snapshot recipient rows at creation — the send's submitted set replaces them,
        // keeping exactly one row per address. Normal drafts have none, so behavior is unchanged for them.
        var preExisting = await _db.Set<EmailRecipient>().Where(r => r.TransferId == transfer.Id).ToListAsync(ct);
        _db.Set<EmailRecipient>().RemoveRange(preExisting);

        _db.Set<Transfer>().Update(transfer);
        foreach (var r in recipients)
            _db.Set<EmailRecipient>().Add(r);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct) =>
        await _db.Set<EmailRecipient>()
            .Where(r => r.TransferId == transferId)
            .Select(r => r.Address)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct) =>
        await _db.Set<FileItem>()
            .AsNoTracking()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FileItemRow(f.Id, f.BlobRefId, f.OriginalName, f.SizeBytes, f.ContentType, f.SortOrder))
            .ToListAsync(ct);

    /// <summary>
    /// T-010-01 (AC-010-1): steps 5–8 in ONE transaction — insert the new draft Transfer + new FileItem rows, then a
    /// guarded atomic RefCount++ per shared blob (ExecuteUpdate with the guard in the WHERE, same style as
    /// PublicTransferStore.RecordSingleFileDownloadAsync; no data copy, ADR-009). rowsAffected &lt; 1 → InvalidOperationException.
    /// T-010-02 inserts its FILES_GONE gate at step 0 inside this same transaction (full rollback — AC-010-2).
    /// </summary>
    public async Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.Set<Transfer>().Add(draft);
        foreach (var f in fileItems)
            _db.Set<FileItem>().Add(f);
        foreach (var r in recipients)
            _db.Set<EmailRecipient>().Add(r); // T-010-01: materialized snapshot rows — the editable pending state US-010-02 reads
        await _db.SaveChangesAsync(ct);

        // The guard lives in the UPDATE's WHERE (Id must exist); a concurrent physical delete makes rowsAffected 0.
        foreach (var blobRef in sharedBlobRefs)
        {
            var rowsAffected = await _db.Set<BlobRef>()
                .Where(b => b.Id == blobRef.Id)
                .ExecuteUpdateAsync(setter => setter.SetProperty(b => b.RefCount, b => b.RefCount + 1), ct);

            if (rowsAffected is not 1)
                throw new InvalidOperationException($"BlobRef {blobRef.Id} unavailable — the file's blob row is gone.");
        }

        await tx.CommitAsync(ct);
    }
}
