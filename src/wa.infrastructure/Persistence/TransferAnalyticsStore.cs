using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Analytics;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-023-01 (F-PRF-003): thin adapter over WaDbContext for the analytics read path. Three GROUP BY queries, all
/// WHERE TransferId = @id AND CreatedAtUtc >= @since (seek on IX_DownEvent_T), AsNoTracking; no raw rows leave SQL (EC-023-4).
/// </summary>
public sealed class TransferAnalyticsStore : ITransferAnalyticsStore
{
    private readonly WaDbContext _db;

    public TransferAnalyticsStore(WaDbContext db) => _db = db;

    public Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct) =>
        _db.Set<Transfer>()
            .AsNoTracking()
            .Where(t => t.Id == transferId && t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
            .Select(t => new AnalyticsTransfer(t.Id, t.CreatedAtUtc, t.Status))
            .FirstOrDefaultAsync(ct);

    public async Task<AnalyticsAggregate> AggregateAsync(Guid transferId, DateTime sinceUtc, CancellationToken ct)
    {
        var events = _db.Set<DownloadEvent>().AsNoTracking();

        // Sequential: the singleton store shares one WaDbContext.
        var daily = await DailyQuery(events, transferId, sinceUtc).ToListAsync(ct);
        var countries = await Scope(events, transferId, sinceUtc)
            .GroupBy(e => e.Country)
            .Select(g => new KeyCount(g.Key, g.Count()))
            .ToListAsync(ct);
        var browsers = await Scope(events, transferId, sinceUtc)
            .GroupBy(e => e.UserAgentFamily)
            .Select(g => new KeyCount(g.Key, g.Count()))
            .ToListAsync(ct);

        return new AnalyticsAggregate(
            daily.Select(d => new DailyCount(DateOnly.FromDateTime(d.Day), d.Count)).ToList(),
            countries.Select(c => (c.Key, c.Count)).ToList(),
            browsers.Select(b => (b.Key, b.Count)).ToList());
    }

    /// <summary>GROUP BY CONVERT(date, CreatedAtUtc). Public so the integration test can inspect the real plan (IX_DownEvent_T seek).</summary>
    public static IQueryable<DayCount> DailyQuery(IQueryable<DownloadEvent> events, Guid transferId, DateTime sinceUtc) =>
        Scope(events, transferId, sinceUtc)
            .GroupBy(e => e.CreatedAtUtc.Date)
            .Select(g => new DayCount(g.Key, g.Count()));

    private static IQueryable<DownloadEvent> Scope(IQueryable<DownloadEvent> events, Guid transferId, DateTime sinceUtc) =>
        events.Where(e => e.TransferId == transferId && e.CreatedAtUtc >= sinceUtc);

    /// <summary>SQL-side projection: a UTC calendar day and its count.</summary>
    public sealed record DayCount(DateTime Day, int Count);

    private sealed record KeyCount(string? Key, int Count);
}
