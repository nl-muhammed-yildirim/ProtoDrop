using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;
using wa.domain.Transfers; // T-011-04: CursorCodec (TA-4.1.4 pin) for the nextCursor

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): platform-wide admin transfer search — single bounded query over WaDbContext.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class SearchTransfersStore : ISearchTransfersStore
{
    private readonly WaDbContext _db;

    public SearchTransfersStore(WaDbContext db) => _db = db;

    /// <summary>
    /// T-011-04 (TA-4.2#21): single bounded query mirroring ListByOwnerAsync's shape — OrderByDescending(CreatedAtUtc).ThenByDescending(Id)
    /// + cursor predicate (CreatedAtUtc &lt; @afterUtc OR (= AND Id &lt; @afterIdExcluded)) + Take(limit+1) for next-page detection.
    /// q semantics (DECISION): OR in ONE predicate — t.LinkId == q OR EXISTS EmailRecipient Address LIKE %q% — satisfies both gherkin cases
    /// with one param and no shape sniffing. LinkId equality is case-insensitive via SQL Server CI collation (same idiom as TransferStore L22).
    /// Owner email via the OverviewStore subquery idiom (AppUser where Id == t.OwnerAppUserId, FirstOrDefault — null for guest transfers).
    /// </summary>
    public async Task<(List<AdminTransferRow> Items, string? NextCursor)> SearchAsync(
        string? q,
        IReadOnlyCollection<byte>? statuses,
        long? minBytes,
        long? maxBytes,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct)
    {
        var rows = await _db.Set<Transfer>()
            .AsNoTracking()
            // T-011-04 (DECISION): q predicate — LinkId exact OR any EmailRecipient.Address substring (EXISTS subquery).
            .Where(t => q == null || t.LinkId == q || _db.Set<EmailRecipient>().Any(r => r.TransferId == t.Id && r.Address.Contains(q)))
            // T-011-04: status predicate only when a byte set is given — null = all statuses including deleted (DECISION).
            .Where(t => statuses == null || statuses.Contains(t.Status))
            // T-011-04: size range — inclusive bounds, null = no bound.
            .Where(t => minBytes == null || t.TotalBytes >= minBytes.Value)
            .Where(t => maxBytes == null || t.TotalBytes <= maxBytes.Value)
            // T-011-04: date range — inclusive bounds, null = no bound.
            .Where(t => fromUtc == null || t.CreatedAtUtc >= fromUtc.Value)
            .Where(t => toUtc == null || t.CreatedAtUtc <= toUtc.Value)
            // TA-4.1.4: cursor predicate — exactly as ListByOwnerAsync L65.
            .Where(t => afterUtc == null || t.CreatedAtUtc < afterUtc.Value || (t.CreatedAtUtc == afterUtc.Value && t.Id < afterIdExcluded!.Value))
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Take(limit + 1)
            .Select(t => new AdminTransferRow(
                t.Id,
                t.LinkId,
                _db.Set<AppUser>().Where(u => u.Id == t.OwnerAppUserId).Select(u => u.Email).FirstOrDefault(),
                t.Status,
                t.FileCount,
                t.TotalBytes,
                t.CreatedAtUtc,
                t.DownloadsCount))
            .ToListAsync(ct);

        var items = rows.Take(limit).ToList();
        // TA-4.1.4: cursor = base64("{createdAtUtc:O}:{id:N}") of the last returned row — null when no next page.
        var nextCursor = rows.Count > limit
            ? CursorCodec.Encode(rows[limit - 1].CreatedAtUtc, rows[limit - 1].Id)
            : null;

        return (items, nextCursor);
    }
}
