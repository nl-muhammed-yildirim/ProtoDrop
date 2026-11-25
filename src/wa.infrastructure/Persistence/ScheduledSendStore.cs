using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-022-04 (FR-022-5, TA-6.1): adapter for the f-expire schedule sub-step. A fresh WaDbContext per call
/// (IDbContextFactory), like BillingSequenceStore. No schema change: reuses JobRun (JobKey "schedule") and IX_Transfer_Sched.
/// </summary>
public sealed class ScheduledSendStore : IScheduledSendStore
{
    private const string JobKey = "schedule";

    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="ScheduledSendStore"/> class.</summary>
    /// <param name="factory">The factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public ScheduledSendStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO [JobRun] ([JobKey], [RunAtUtc], [LockUntilUtc]) VALUES ({0}, {1}, {2})",
                [JobKey, runWindowStartUtc, lockUntilUtc], ct);
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Duplicate PK_JobRun — another instance claimed this window (AC-022-6).
            return false;
        }
    }

    /// <summary>
    /// The due scan. Mirrors <c>ScheduledSend.IsDue</c> (EF cannot translate the call). Reads only Id + ScheduledSendAtUtc so it stays
    /// covered by IX_Transfer_Sched (Status, ScheduledSendAtUtc) — an index seek with no lookup; do not add other columns.
    /// Public static so the integration plan test runs the exact query.
    /// </summary>
    /// <param name="transfers">The transfers.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <returns>The i queryable of due scheduled row.</returns>
    public static IQueryable<DueScheduledRow> DueQuery(IQueryable<Transfer> transfers, DateTime nowUtc) =>
        transfers
            .Where(t => t.Status == TransferStatus.Scheduled && t.ScheduledSendAtUtc <= nowUtc)
            .OrderBy(t => t.ScheduledSendAtUtc)
            .Select(t => new DueScheduledRow(t.Id, t.ScheduledSendAtUtc!.Value));

    /// <inheritdoc />
    public async Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await DueQuery(db.Set<Transfer>().AsNoTracking(), nowUtc).Take(batchSize).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Second guard (AC-022-6): idempotent conditional flip. Mirrors ScheduledSend.CanActivate — expiry wins (AC-022-5).
        var updated = await db.Set<Transfer>()
            .Where(t => t.Id == transferId
                        && t.Status == TransferStatus.Scheduled
                        && t.ScheduledSendAtUtc <= nowUtc
                        && t.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TransferStatus.Active), ct);
        if (updated != 1)
            return null;

        var row = await db.Set<Transfer>().AsNoTracking()
            .Where(t => t.Id == transferId)
            .Select(t => new { t.LinkId, t.TotalBytes, t.FileCount, t.SenderEmail, t.ScheduledSendAtUtc })
            .FirstAsync(ct);
        var emails = await db.Set<EmailRecipient>().AsNoTracking()
            .Where(r => r.TransferId == transferId)
            .OrderBy(r => r.Address)
            .Select(r => r.Address)
            .ToListAsync(ct);

        return new ActivatedTransfer(
            transferId, row.LinkId, row.TotalBytes, row.FileCount, row.SenderEmail,
            row.ScheduledSendAtUtc ?? nowUtc, emails);
    }

    /// <inheritdoc />
    public async Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<JobRun>()
            .Where(j => j.JobKey == JobKey && j.RunAtUtc == runWindowStartUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.FinishedAtUtc, finishedAtUtc), ct);
    }
}
