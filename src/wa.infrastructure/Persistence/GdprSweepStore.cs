using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Gdpr;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>US-047-02: worker-side adapter for <see cref="IGdprSweepStore"/> (f-gdpr-sweep, TA-6.1/6.3). A fresh WaDbContext per call.</summary>
public sealed class GdprSweepStore : IGdprSweepStore
{
    private const string JobKey = "gdpr-sweep";

    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="GdprSweepStore"/> class.</summary>
    /// <param name="factory">Factory that creates a short-lived database context per call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public GdprSweepStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc]) VALUES ({0}, {1}, {2})",
                [JobKey, windowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            return false; // another instance claimed this window
        }
    }

    /// <inheritdoc />
    public async Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var jobRun = await db.Set<JobRun>().FindAsync([JobKey, windowStartUtc], ct);
        if (jobRun is not null)
        {
            jobRun.FinishedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deletedBeforeUtc, int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<Erasure>().AsNoTracking()
            .Where(e => e.SweptAtUtc == null && e.DeletedAtUtc < deletedBeforeUtc)
            .OrderBy(e => e.DeletedAtUtc)
            .Take(batchSize)
            .Select(e => e.AppUserId)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task MarkSweptAsync(Guid userId, DateTime sweptAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<Erasure>()
            .Where(e => e.AppUserId == userId && e.SweptAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.SweptAtUtc, sweptAtUtc), ct);
    }
}
