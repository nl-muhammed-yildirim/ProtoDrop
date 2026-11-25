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

    /// <summary>Initializes a new instance of the <see cref="TransferAnalyticsStore"/> class.</summary>
    /// <param name="db">Database context that backs the store.</param>
    public TransferAnalyticsStore(WaDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct) =>
        _db.Set<Transfer>()
            .AsNoTracking()
            .Where(t => t.Id == transferId && t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
            .Select(t => new AnalyticsTransfer(t.Id, t.CreatedAtUtc, t.Status))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
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
        var fileRows = await FileQuery(events, transferId, sinceUtc).ToListAsync(ct);
        var files = await _db.Set<FileItem>()
            .AsNoTracking()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new AnalyticsFile(f.Id, f.OriginalName, f.SortOrder))
            .ToListAsync(ct);

        return new AnalyticsAggregate(
            daily.Select(d => new DailyCount(DateOnly.FromDateTime(d.Day), d.Count)).ToList(),
            countries.Select(c => (c.Key, c.Count)).ToList(),
            browsers.Select(b => (b.Key, b.Count)).ToList(),
            files,
            fileRows.Select(r => new FileCountRow(r.FileId, r.Count, r.LastAtUtc)).ToList());
    }

    /// <summary>GROUP BY CONVERT(date, CreatedAtUtc). Public so the integration test can inspect the real plan (IX_DownEvent_T seek).</summary>
    /// <param name="events">Download events to query.</param>
    /// <param name="transferId">Transfer whose events are counted.</param>
    /// <param name="sinceUtc">Only events at or after this instant are counted.</param>
    /// <returns>A query that yields one count per UTC day.</returns>
    public static IQueryable<DayCount> DailyQuery(IQueryable<DownloadEvent> events, Guid transferId, DateTime sinceUtc) =>
        Scope(events, transferId, sinceUtc)
            .GroupBy(e => e.CreatedAtUtc.Date)
            .Select(g => new DayCount(g.Key, g.Count()));

    /// <summary>GROUP BY FileId (NULL = download-all) with count and latest event time. Public for the plan-inspection test.</summary>
    /// <param name="events">Download events to query.</param>
    /// <param name="transferId">Transfer whose events are counted.</param>
    /// <param name="sinceUtc">Only events at or after this instant are counted.</param>
    /// <returns>A query that yields one count and latest event time per file, with a null file for download-all.</returns>
    public static IQueryable<FileKeyCount> FileQuery(IQueryable<DownloadEvent> events, Guid transferId, DateTime sinceUtc) =>
        Scope(events, transferId, sinceUtc)
            .GroupBy(e => e.FileId)
            .Select(g => new FileKeyCount(g.Key, g.Count(), g.Max(e => e.CreatedAtUtc)));

    private static IQueryable<DownloadEvent> Scope(IQueryable<DownloadEvent> events, Guid transferId, DateTime sinceUtc) =>
        events.Where(e => e.TransferId == transferId && e.CreatedAtUtc >= sinceUtc);

    /// <summary>SQL-side projection: a UTC calendar day and its count.</summary>
    /// <param name="Day">UTC calendar day.</param>
    /// <param name="Count">Number of download events on that day.</param>
    public sealed record DayCount(DateTime Day, int Count);

    /// <summary>SQL-side projection: a FileId (null = download-all), its count and last event time.</summary>
    /// <param name="FileId">File the downloads belong to; null for download-all events.</param>
    /// <param name="Count">Number of download events for that file.</param>
    /// <param name="LastAtUtc">UTC time of the most recent event.</param>
    public sealed record FileKeyCount(Guid? FileId, int Count, DateTime LastAtUtc);

    private sealed record KeyCount(string? Key, int Count);
}
