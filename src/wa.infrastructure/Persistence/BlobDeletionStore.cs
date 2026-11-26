using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-005-03: thin adapter over WaDbContext implementing IBlobDeletionStore — the f-delete-blobs job's DB surface (TA-6.5).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class BlobDeletionStore(IDbContextFactory<WaDbContext> factory, TimeProvider timeProvider) : IBlobDeletionStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            // Raw SQL INSERT — bypasses the EF change tracker so the duplicate-PK race is caught by SqlException.
            await db.Database.ExecuteSqlRawAsync(
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<BlobRef>()
            .AsNoTracking()
            .Where(b => b.RefCount == 0 && b.PhysicallyDeletedAtUtc < cutoffUtc)
            .OrderBy(b => b.PhysicallyDeletedAtUtc)
            .Take(batchSize)
            .Select(b => new BlobDeletionBatchRow(b.Id, b.BlobPath))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task DeleteRowAsync(Guid blobRefId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var blob = await db.Set<BlobRef>().FirstAsync(b => b.Id == blobRefId, ct);
        db.Set<BlobRef>().Remove(blob);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var jobRun = db.Set<JobRun>().Find("delete-blobs", runWindowStartUtc);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }
}
