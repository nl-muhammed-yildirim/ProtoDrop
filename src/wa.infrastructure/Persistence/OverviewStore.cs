using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-03 (TA-4.2#20 / FR-011-6): platform-wide overview aggregates — six bounded queries over WaDbContext.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class OverviewStore(IDbContextFactory<WaDbContext> factory) : IOverviewStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    /// <remarks>
    /// AC-011-1: four numbers + recent-transfers list. Windows are passed IN (UTC day boundary is unit-testable).
    /// </remarks>
    public async Task<OverviewData> GetAsync(DateTime todayStartUtc, DateTime emailsWindowStartUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // (a) Active transfers — Status=1 per FR-011-2's literal, plus Scheduled (5): a live transfer awaiting its send time (F-PRF-002 review).
        var activeTransfers = await db.Set<Transfer>()
            .CountAsync(t => t.Status == 1 || t.Status == TransferStatus.Scheduled, ct);

        // (b) Storage bytes used — platform-wide variant of TransferStore.GetActiveStorageBytesAsync (T-010-04 idiom):
        // Status IN (1,3), no owner filter, three bounded queries (active ids → distinct blob ids → SUM of SizeBytes).
        var storageBytesUsed = await GetPlatformStorageBytesAsync(db, ct);

        // (c) New users today — ALL AppUser created in the UTC day regardless of DeletedAtUtc (a signup is a signup).
        var newUsersToday = await db.Set<AppUser>()
            .CountAsync(u => u.CreatedAtUtc >= todayStartUtc && u.CreatedAtUtc < todayStartUtc.AddDays(1), ct);

        // (d) Emails sent in the last 7 days — count of EmailRecipient.NotifiedAtUtc in the window (US-011-01 approximation).
        var emailsSent7d = await db.Set<EmailRecipient>()
            .CountAsync(r => r.NotifiedAtUtc >= emailsWindowStartUtc, ct);

        // (e) Emails failed in the last 7 days — count of email.failed outbox rows (no DLQ table exists; the only failure record).
        var emailsFailed7d = await db.Set<DomainEvent>()
            .CountAsync(ev => ev.EventType == "email.failed" && ev.OccurredAtUtc >= emailsWindowStartUtc, ct);

        // (g) T-013-05 (AC-013-3): email health surface — pending recipients (NotifiedAtUtc null = never notified).
        var emailPendingCount = await db.Set<EmailRecipient>()
            .CountAsync(r => r.NotifiedAtUtc == null, ct);

        // (h) T-013-05: all-time email.failed outbox count — the health surface's failed total (no DLQ table exists; comment at L38).
        var emailFailedTotal = await db.Set<DomainEvent>()
            .CountAsync(ev => ev.EventType == "email.failed", ct);

        // (i) T-013-05: last successful delivery — max(NotifiedAtUtc), null when nothing was ever delivered.
        var lastSuccessfulDeliveryAge = await db.Set<EmailRecipient>()
            .Where(r => r.NotifiedAtUtc != null)
            .MaxAsync(r => (DateTime?)r.NotifiedAtUtc, ct);

        // (f) Recent transfers — last 20 by CreatedAtUtc desc; owner email via subquery FirstOrDefault on AppUser.
        var recent = await db.Set<Transfer>()
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(20)
            .Select(t => new RecentTransferItem(
                t.LinkId,
                db.Set<AppUser>().Where(u => u.Id == t.OwnerAppUserId).Select(u => u.Email).FirstOrDefault(),
                t.Status,
                t.TotalBytes,
                t.CreatedAtUtc))
            .ToListAsync(ct);

        return new OverviewData(activeTransfers, storageBytesUsed, newUsersToday, emailsSent7d, emailsFailed7d, emailPendingCount, emailFailedTotal, lastSuccessfulDeliveryAge, recent);
    }

    /// <summary>
    /// Platform-wide storage bytes — mirrors TransferStore.GetActiveStorageBytesAsync (T-010-04) but drops the owner filter.
    /// Status IN (1,3) so the overview matches what F-TRF-007 counts as in-use blobs (DownloadLimit transfers still serve files).
    /// </summary>
    private static async Task<long> GetPlatformStorageBytesAsync(WaDbContext db, CancellationToken ct)
    {
        // (a) active transfer ids — Status IN (1,3), no owner filter.
        var activeIds = db.Set<Transfer>()
            .Where(t => t.Status == 1 || t.Status == 3)
            .Select(t => t.Id);

        // (b) distinct blob ids those transfers' FileItems reference.
        var blobIds = db.Set<FileItem>()
            .Where(f => activeIds.Contains(f.TransferId))
            .Select(f => f.BlobRefId)
            .Distinct();

        // (c) sum each physical blob once.
        return await db.Set<BlobRef>()
            .Where(b => blobIds.Contains(b.Id))
            .SumAsync(b => b.SizeBytes, ct);
    }
}
