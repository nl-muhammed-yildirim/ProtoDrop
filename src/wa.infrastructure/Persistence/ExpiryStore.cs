using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-005-01: thin adapter over WaDbContext implementing IExpiryStore — the f-expire job's DB surface (TA-6.3).
/// </summary>
public sealed class ExpiryStore : IExpiryStore
{
    private readonly WaDbContext _db;

    public ExpiryStore(WaDbContext db) => _db = db;

    public async Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        try
        {
            // Raw SQL INSERT — bypasses the EF change tracker (singleton DbContext shared across invocations).
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc]) VALUES ({0}, {1}, {2})",
                ["expire", runWindowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Duplicate PK_JobRun — another instance claimed this window. Normal in scale-out.
            return false;
        }
    }

    public async Task<IReadOnlyList<ExpiryBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        return await _db.Set<Transfer>()
            .AsNoTracking()
            .Where(t => t.Status == 1 && t.ExpiresAtUtc < cutoffUtc)
            .OrderBy(t => t.ExpiresAtUtc)
            .Take(batchSize)
            .Select(t => new ExpiryBatchRow(t.Id, t.LinkId))
            .ToListAsync(ct);
    }

    public async Task<int> MarkExpiredAsync(IReadOnlyList<Guid> transferIds, DateTime expiredAtUtc, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.Set<Transfer>()
            .Where(t => transferIds.Contains(t.Id) && t.Status == 1) // TA-6.3 note 5: must not fight Status=3
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (byte)2)
                .SetProperty(t => t.ExpiredAtUtc, expiredAtUtc), ct);
        await tx.CommitAsync(ct);
        return updated;
    }

    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
    {
        var jobRun = _db.Set<JobRun>().Find("expire", runWindowStartUtc);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }
}
