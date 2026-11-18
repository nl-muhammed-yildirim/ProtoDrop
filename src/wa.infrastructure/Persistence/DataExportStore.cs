using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.DataExport;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-047-01: API-side adapter for <see cref="IDataExportStore"/>. A fresh WaDbContext per call, so a failed INSERT
/// (unique violation) never stays tracked on a shared context (BillingSequenceStore precedent).
/// </summary>
public sealed class DataExportStore : IDataExportStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    public DataExportStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<DataExport?> GetLatestAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<DataExport>().AsNoTracking()
            .Where(e => e.AppUserId == userId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryCreateAsync(DataExport row, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<DataExport>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            return false; // UX_DataExport_InFlight: another processing export exists
        }
    }
}

/// <summary>US-047-01: worker-side adapter for <see cref="IDataExportJobStore"/> (f-data-export, TA-6.1/6.3).</summary>
public sealed class DataExportJobStore : IDataExportJobStore
{
    private const string JobKey = "data-export";

    private readonly IDbContextFactory<WaDbContext> _factory;

    public DataExportJobStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

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

    public async Task<IReadOnlyList<DataExport>> GetProcessingAsync(int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<DataExport>().AsNoTracking()
            .Where(e => e.State == DataExportState.Processing)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<ExportSnapshot?> ReadSnapshotAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var profile = await (
            from u in db.Set<AppUser>().AsNoTracking()
            join p in db.Set<Plan>().AsNoTracking() on u.PlanId equals p.Id
            where u.Id == userId && u.DeletedAtUtc == null
            select new ExportProfile(u.Email, u.DisplayName, u.Theme, p.Code, u.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);
        if (profile is null)
            return null;

        // Owned transfers only (AC-047-3) — every row we still hold, deleted ones included (status "deleted").
        var transfers = await db.Set<Transfer>().AsNoTracking()
            .Where(t => t.OwnerAppUserId == userId)
            .OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.CreatedAtUtc, t.Status, t.ExpiresAtUtc, PasswordSet = t.PasswordHash != null, t.DownloadsCount })
            .ToListAsync(ct);

        var ids = transfers.Select(t => t.Id).ToList();
        var files = ids.Count == 0
            ? []
            : await db.Set<FileItem>().AsNoTracking()
                .Where(f => ids.Contains(f.TransferId))
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .Select(f => new { f.TransferId, f.OriginalName, f.SizeBytes })
                .ToListAsync(ct);
        var recipients = ids.Count == 0
            ? []
            : await db.Set<EmailRecipient>().AsNoTracking()
                .Where(r => ids.Contains(r.TransferId))
                .OrderBy(r => r.Id)
                .Select(r => new { r.TransferId, r.Address })
                .ToListAsync(ct);

        var filesBy = files.ToLookup(f => f.TransferId);
        var recipientsBy = recipients.ToLookup(r => r.TransferId);
        var rows = transfers.Select(t => new ExportTransfer(
            t.Id,
            DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc),
            t.Status,
            DateTime.SpecifyKind(t.ExpiresAtUtc, DateTimeKind.Utc),
            t.PasswordSet,
            t.DownloadsCount,
            filesBy[t.Id].Select(f => new ExportFile(f.OriginalName, f.SizeBytes)).ToList(),
            recipientsBy[t.Id].Select(r => r.Address).ToList())).ToList();

        return new ExportSnapshot(profile with { CreatedAtUtc = DateTime.SpecifyKind(profile.CreatedAtUtc, DateTimeKind.Utc) }, rows);
    }

    public async Task MarkReadyAsync(Guid id, string blobPath, DateTime readyAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<DataExport>()
            .Where(e => e.Id == id && e.State == DataExportState.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.State, DataExportState.Ready)
                .SetProperty(e => e.BlobPath, blobPath)
                .SetProperty(e => e.ReadyAtUtc, readyAtUtc), ct);
    }

    public async Task MarkFailedAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<DataExport>()
            .Where(e => e.Id == id && e.State == DataExportState.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.State, DataExportState.Failed), ct);
    }

    public async Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Set<DataExport>().AsNoTracking()
            .Where(e => e.State == DataExportState.Ready && e.ReadyAtUtc < readyBeforeUtc && e.BlobPath != null)
            .OrderBy(e => e.ReadyAtUtc)
            .Take(batchSize)
            .Select(e => new { e.Id, e.BlobPath })
            .ToListAsync(ct);
        return rows.Select(r => new PurgeableExport(r.Id, r.BlobPath!)).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<DataExport>().Where(e => e.Id == id).ExecuteDeleteAsync(ct);
    }
}
