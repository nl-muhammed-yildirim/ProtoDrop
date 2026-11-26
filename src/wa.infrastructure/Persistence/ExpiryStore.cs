using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-005-01: thin adapter over WaDbContext implementing IExpiryStore — the f-expire job's DB surface (TA-6.3).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class ExpiryStore(IDbContextFactory<WaDbContext> factory, TimeProvider timeProvider) : IExpiryStore
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
                ["expire", runWindowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Duplicate PK_JobRun — another instance claimed this window. Normal in scale-out.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpiryBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<Transfer>()
            .AsNoTracking()
            .Where(t => (t.Status == TransferStatus.Active || t.Status == TransferStatus.Scheduled) && t.ExpiresAtUtc < cutoffUtc)
            .OrderBy(t => t.ExpiresAtUtc)
            .Take(batchSize)
            .Select(t => new ExpiryBatchRow(t.Id, t.LinkId))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> MarkExpiredAsync(IReadOnlyList<Guid> transferIds, DateTime expiredAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.Set<Transfer>()
            .Where(t => transferIds.Contains(t.Id) && (t.Status == TransferStatus.Active || t.Status == TransferStatus.Scheduled)) // TA-6.3 note 5: must not fight Status=3
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, (byte)2)
                .SetProperty(t => t.ExpiredAtUtc, expiredAtUtc), ct);
        await tx.CommitAsync(ct);
        return updated;
    }

    /// <inheritdoc />
    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var jobRun = db.Set<JobRun>().Find("expire", runWindowStartUtc);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }
}
