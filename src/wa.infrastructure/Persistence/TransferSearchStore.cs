using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Transfers; // US-045-01 (D2/D3): SearchTerm rank constants + SearchCursorCodec

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-045-01 (FR-045-1/3/7, D-21 option B): owner-scoped transfer search — literal case-insensitive substring match via EF Contains over FileItem.OriginalName / EmailRecipient.Address / Transfer.SenderName; rank = CASE over EXISTS subqueries
/// (file=3 &gt; recipient=2 &gt; sender=1); order rank DESC, CreatedAtUtc DESC, Id DESC; Take(limit+1) for next-page detection. No schema change, no migration — the owner predicate bounds the scan to one user's rows.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext), mirroring ITransferStore.
/// </summary>
public sealed class TransferSearchStore : ITransferSearchStore
{
    private readonly WaDbContext _db;

    public TransferSearchStore(WaDbContext db) => _db = db;

    /// <summary>FR-045-1/3/7: one row per transfer (de-dup by construction — the rank is a CASE over EXISTS subqueries); search cursor bound to the term.</summary>
    public async Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
        Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
        int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
    {
        var rows = await _db.Set<Transfer>()
            .AsNoTracking()
            // FR-045-7: owner scope in the predicate — search never crosses owners.
            .Where(t => t.OwnerAppUserId == ownerId && t.DeletedAtUtc == null)
            // T-009-02 idiom: status predicate only when a byte set is given — null = all (AND combination with q, plan §5).
            .Where(t => statuses == null || statuses.Contains(t.Status))
            .Select(t => new
            {
                T = t,
                // D2 (FR-045-3 / EC-045-6): rank = max of the matched fields — file name 3 > recipient 2 > sender 1. Literal characters only: EF sends Contains as an escaped LIKE/CHARINDEX (no operator leakage).
                Rank = _db.Set<FileItem>().Any(f => f.TransferId == t.Id && f.OriginalName.Contains(term)) ? SearchTerm.RankFileName
                    : _db.Set<EmailRecipient>().Any(r => r.TransferId == t.Id && r.Address.Contains(term)) ? SearchTerm.RankRecipient
                    : t.SenderName.Contains(term) ? SearchTerm.RankSender
                    : 0
            })
            .Where(x => x.Rank > 0)   // only matching transfers — the term is guaranteed non-empty by the handler.
            // FR-045-3: search cursor predicate (rank DESC, then CreatedAtUtc/Id keyset). afterRank null = first page.
            .Where(x => afterRank == null
                || x.Rank < afterRank.Value
                || (x.Rank == afterRank.Value && (x.T.CreatedAtUtc < afterUtc!.Value
                    || (x.T.CreatedAtUtc == afterUtc.Value && x.T.Id < afterIdExcluded!.Value))))
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.T.CreatedAtUtc)
            .ThenByDescending(x => x.T.Id)
            .Take(limit + 1)
            // Row projection mirrors TransferStore.ListByOwnerAsync (D6): same MyTransferRow shape, single wire site at the endpoint.
            .Select(x => new
            {
                x.Rank,
                Id = x.T.Id,
                CreatedAtUtc = x.T.CreatedAtUtc,
                Name = _db.Set<FileItem>()
                    .Where(f => f.TransferId == x.T.Id)
                    .OrderBy(f => f.SortOrder)
                    .ThenBy(f => f.Id)
                    .Select(f => f.OriginalName)
                    .FirstOrDefault() ?? "Transfer",
                FileCount = x.T.FileCount,
                TotalBytes = x.T.TotalBytes,
                Status = x.T.Status,
                ExpiresAtUtc = x.T.ExpiresAtUtc,
                DownloadsCount = x.T.DownloadsCount,
                MaxDownloads = x.T.MaxDownloads,
                RecipientCount = _db.Set<EmailRecipient>().Count(r => r.TransferId == x.T.Id),
                LinkId = x.T.LinkId
            })
            .ToListAsync(ct);

        var items = rows.Take(limit)
            .Select(r => new MyTransferRow(r.Id, r.Name, r.FileCount, r.TotalBytes, r.Status, r.ExpiresAtUtc, r.DownloadsCount, r.MaxDownloads, r.RecipientCount, r.LinkId))
            .ToList();
        // FR-045-3: search cursor = SearchCursorCodec of the last returned row — null when no next page. The term binds it to this query (D3).
        var nextCursor = rows.Count > limit
            ? SearchCursorCodec.Encode(term, rows[limit - 1].Rank, rows[limit - 1].CreatedAtUtc, rows[limit - 1].Id)
            : null;

        return (items, nextCursor);
    }
}
