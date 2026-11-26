using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-002-04: thin adapter over WaDbContext implementing ISendStore — the only DB surface the send handler needs.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
/// <param name="blobs">Blob store used to check that re-sent files still physically exist.</param>
public sealed class SendStore(IDbContextFactory<WaDbContext> factory, IBlobStore blobs) : ISendStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;
    private readonly IBlobStore _blobs = blobs;   // T-010-02: the gate primitive — physical-presence checks via ExistsAsync.

    /// <inheritdoc />
    public async Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<Transfer>().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    /// <inheritdoc />
    public async Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // T-010-02 (EC-010-2): re-send drafts only — a normal draft's blobs cannot be "gone" via this lifecycle, so the
        // gate is scoped to SourcedFrom rows. Runs BEFORE any write: a throw rolls the transaction back → no orphan rows.
        if (transfer.SourcedFrom is not null)
        {
            var blobIds = await db.Set<FileItem>()
                .Where(f => f.TransferId == transfer.Id)
                .Select(f => f.BlobRefId)
                .Distinct()
                .ToListAsync(ct);
            await AssertBlobsPhysicallyPresentAsync(db, blobIds, ct); // T-010-02: the shared FILES_GONE gate
        }

        // T-010-01 (AC-010-1): a re-send draft carries SourcedFrom = original.Id — set SupersededBy on the original row
        // inside this same transaction. rowsAffected 0 → the original was deleted while the draft sat open — no error here;
        // EC-010-2's FILES_GONE gate is T-010-02's scope (it inserts its predicate before this write).
        if (transfer.SourcedFrom is not null)
        {
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE [Transfer] SET [SupersededBy] = {0} WHERE [Id] = {1}",
                [transfer.Id, transfer.SourcedFrom.Value], ct);

            // The raw-SQL UPDATE bypasses the change tracker: the original was loaded (tracked) by the earlier resend command, so detach it
            // so a later SaveChanges can't resurrect the old SupersededBy. T-003-10: TransferTrackerExtensions.DetachTransfer, never Local.Remove.
            db.DetachTransfer(transfer.SourcedFrom.Value);
        }

        // T-010-01: re-send drafts materialized snapshot recipient rows at creation — the send's submitted set replaces them,
        // keeping exactly one row per address. Normal drafts have none, so behavior is unchanged for them.
        var preExisting = await db.Set<EmailRecipient>().Where(r => r.TransferId == transfer.Id).ToListAsync(ct);
        db.Set<EmailRecipient>().RemoveRange(preExisting);

        db.Set<Transfer>().Update(transfer);
        foreach (var r in recipients)
            db.Set<EmailRecipient>().Add(r);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<EmailRecipient>()
            .Where(r => r.TransferId == transferId)
            .Select(r => r.Address)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<FileItem>()
            .AsNoTracking()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FileItemRow(f.Id, f.BlobRefId, f.OriginalName, f.SizeBytes, f.ContentType, f.SortOrder))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// T-010-01 (AC-010-1): steps 5–8 in ONE transaction — insert the new draft Transfer + new FileItem rows, then a
    /// guarded atomic RefCount++ per shared blob (ExecuteUpdate with the guard in the WHERE, same style as
    /// PublicTransferStore.RecordSingleFileDownloadAsync; no data copy, ADR-009). rowsAffected &lt; 1 → InvalidOperationException.
    /// T-010-02 inserts its FILES_GONE gate at step 0 inside this same transaction (full rollback — AC-010-2).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the operation is not valid in the current state.
    /// </exception>
    public async Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // T-010-02 (AC-010-2): step 0 — the FILES_GONE gate inside this transaction, BEFORE any insert or RefCount bump.
        // The handler passes Id-only BlobRef stubs, so the predicate reads its own rows through the same context (db). A throw rolls this
        // transaction back → zero new rows, no RefCount change (no half-created draft remains).
        var distinctBlobIds = sharedBlobRefs.Select(b => b.Id).Distinct().ToList();
        await AssertBlobsPhysicallyPresentAsync(db, distinctBlobIds, ct);

        db.Set<Transfer>().Add(draft);
        foreach (var f in fileItems)
            db.Set<FileItem>().Add(f);
        foreach (var r in recipients)
            db.Set<EmailRecipient>().Add(r); // T-010-01: materialized snapshot rows — the editable pending state US-010-02 reads
        await db.SaveChangesAsync(ct);

        // The guard lives in the UPDATE's WHERE (Id must exist); a concurrent physical delete makes rowsAffected 0.
        foreach (var blobRef in sharedBlobRefs)
        {
            var rowsAffected = await db.Set<BlobRef>()
                .Where(b => b.Id == blobRef.Id)
                .ExecuteUpdateAsync(setter => setter.SetProperty(b => b.RefCount, b => b.RefCount + 1), ct);

            if (rowsAffected is not 1)
                throw new InvalidOperationException($"BlobRef {blobRef.Id} unavailable — the file's blob row is gone.");
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// T-010-02 gate — the single shared predicate for create-time (SaveResendAsync step 0) and send-time
    /// (SaveSendAsync, SourcedFrom drafts). Checks PHYSICAL presence only: row presence + IBlobStore.ExistsAsync(row.BlobPath).
    /// A missing BlobRef row means gone — T-005-03's f-delete-blobs job removes blob and row in one pass, so a missing
    /// row has no path to check. PhysicallyDeletedAtUtc is NOT read: within T-005-03's 24 h buffer window the blob is
    /// still physically present and the re-send is ALLOWED (exit check c). Must run inside the caller's transaction —
    /// a FilesGoneException throw rolls it back → zero new rows, no RefCount change (AC-010-2). An empty id collection
    /// returns early (a SourcedFrom draft with no file rows must not fire the gate).
    /// </summary>
    private async Task AssertBlobsPhysicallyPresentAsync(WaDbContext db, List<Guid> blobRefIds, CancellationToken ct)
    {
        if (blobRefIds.Count == 0)
            return;

        var rows = await db.Set<BlobRef>()
            .AsNoTracking()
            .Where(b => blobRefIds.Contains(b.Id))
            .ToListAsync(ct);
        var rowsById = rows.ToDictionary(r => r.Id);

        foreach (var id in blobRefIds)
        {
            if (!rowsById.TryGetValue(id, out var row))
                throw new FilesGoneException(); // missing row = gone (f-delete-blobs removed blob and row together)

            var physicallyPresent = await _blobs.ExistsAsync(row.BlobPath, ct);
            if (!physicallyPresent)
                throw new FilesGoneException();
        }
    }
}
