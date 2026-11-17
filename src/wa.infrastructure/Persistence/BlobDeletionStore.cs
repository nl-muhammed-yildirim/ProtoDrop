using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-005-03: thin adapter over WaDbContext implementing IBlobDeletionStore — the f-delete-blobs job's DB surface (TA-6.5).
/// </summary>
public sealed class BlobDeletionStore : IBlobDeletionStore
{
    private readonly WaDbContext _db;

    public BlobDeletionStore(WaDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        try
        {
            // Raw SQL INSERT — bypasses the EF change tracker (singleton DbContext shared across invocations).
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc]) VALUES ({0}, {1}, {2})",
                ["delete-blobs", runWindowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Duplicate PK_JobRun — another instance claimed this window. Normal in scale-out.
            return false;
        }
    }

    public async Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await _db.Set<BlobRef>()
            .AsNoTracking()
            .Where(b => b.RefCount == 0 && b.PhysicallyDeletedAtUtc < cutoffUtc)
            .OrderBy(b => b.PhysicallyDeletedAtUtc)
            .Take(batchSize)
            .Select(b => new BlobDeletionBatchRow(b.Id, b.BlobPath))
            .ToListAsync(ct);
    }

    public async Task DeleteRowAsync(Guid blobRefId, CancellationToken ct)
    {
        var blob = await _db.Set<BlobRef>().FirstAsync(b => b.Id == blobRefId, ct);
        _db.Set<BlobRef>().Remove(blob);
        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
    {
        var jobRun = _db.Set<JobRun>().Find("delete-blobs", runWindowStartUtc);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
