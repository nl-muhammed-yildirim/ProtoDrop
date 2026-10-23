using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-005-02: thin adapter over WaDbContext implementing IExpiryDeletionStore — the f-delete-transfers job's DB surface (TA-6.4).
/// </summary>
public sealed class ExpiryDeletionStore : IExpiryDeletionStore
{
    private readonly WaDbContext _db;

    public ExpiryDeletionStore(WaDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        try
        {
            // Raw SQL INSERT — bypasses the EF change tracker (singleton DbContext shared across invocations).
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc]) VALUES ({0}, {1}, {2})",
                ["delete-transfers", runWindowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Duplicate PK_JobRun — another instance claimed this window. Normal in scale-out.
            return false;
        }
    }

    public async Task<IReadOnlyList<TransferDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await _db.Set<Transfer>()
            .AsNoTracking()
            .Where(t => (t.Status == 2 || t.Status == 3) && t.ExpiredAtUtc < cutoffUtc)
            .OrderBy(t => t.ExpiredAtUtc)
            .Take(batchSize)
            .Select(t => new TransferDeletionBatchRow(t.Id, t.LinkId))
            .ToListAsync(ct);
    }

    public async Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Load FileItems for the affected transfers (need BlobRefId to decrement RefCount).
        var fileItemBlobRefs = await _db.Set<FileItem>()
            .Where(f => transferIds.Contains(f.TransferId))
            .Select(f => f.BlobRefId)
            .ToListAsync(ct);

        // 2. Mark transfers as deleted (guard: only Status IN (2,3)).
        var updated = await _db.Set<Transfer>()
            .Where(t => transferIds.Contains(t.Id) && (t.Status == 2 || t.Status == 3))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (byte)4)
                .SetProperty(t => t.DeletedAtUtc, deletedAtUtc), ct);

        // 3. Decrement BlobRef.RefCount per affected blob; set PhysicallyDeletedAtUtc when it hits 0.
        if (fileItemBlobRefs.Count > 0)
        {
            var distinctBlobIds = fileItemBlobRefs.Distinct().ToList();
            var nowPlusBuffer = deletedAtUtc.AddHours(24);

            // Get current RefCounts to determine which will hit zero.
            var blobRefs = await _db.Set<BlobRef>()
                .Where(b => distinctBlobIds.Contains(b.Id))
                .ToListAsync(ct);

            foreach (var blob in blobRefs)
            {
                var decrementCount = fileItemBlobRefs.Count(r => r == blob.Id);
                blob.RefCount -= decrementCount;
                if (blob.RefCount <= 0)
                {
                    blob.RefCount = 0;
                    blob.PhysicallyDeletedAtUtc = nowPlusBuffer; // EC-005-4: 24h buffer before physical deletion
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        // 4. Delete EmailRecipient rows for the affected transfers.
        var emailRecipients = await _db.Set<EmailRecipient>()
            .Where(e => transferIds.Contains(e.TransferId))
            .ToListAsync(ct);
        _db.Set<EmailRecipient>().RemoveRange(emailRecipients);
        await _db.SaveChangesAsync(ct);

        // 5. Delete FileItem rows for the affected transfers.
        var fileItems = await _db.Set<FileItem>()
            .Where(f => transferIds.Contains(f.TransferId))
            .ToListAsync(ct);
        _db.Set<FileItem>().RemoveRange(fileItems);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return updated;
    }

    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
    {
        var jobRun = _db.Set<JobRun>().Find("delete-transfers", runWindowStartUtc);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
