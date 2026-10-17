using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-014: thin adapter over WaDbContext implementing IPublicTransferStore — the only DB surface the recipient-page handler needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class PublicTransferStore : IPublicTransferStore
{
    private readonly WaDbContext _db;

    public PublicTransferStore(WaDbContext db) => _db = db;

    public async Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct)
    {
        var transfer = await _db.Set<Transfer>().FirstOrDefaultAsync(t => t.LinkId == linkId, ct);
        if (transfer is null) return (null, []);

        // FileItem has no navigation from Transfer — separate query, projected to plain rows (no EF entities in use cases; golden rule 8).
        var files = await _db.Set<FileItem>()
            .Where(f => f.TransferId == transfer.Id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new PublicFileItem(f.Id, f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);
        return (transfer, files);
    }
}
