using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-004-01: thin adapter over WaDbContext implementing IZipFileSource — loads files by transfer Id for the f-zip function.
/// </summary>
/// <param name="db">Database context that backs the store.</param>
public sealed class ZipFileSource(WaDbContext db) : IZipFileSource
{
    private readonly WaDbContext _db = db;

    /// <inheritdoc />
    public async Task<(bool Found, IReadOnlyList<PublicFileItem> Files)> GetFilesByTransferIdAsync(Guid transferId, CancellationToken ct)
    {
        var transfer = await _db.Set<Transfer>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return (false, []);

        var files = await _db.Set<FileItem>()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new PublicFileItem(f.Id, f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);
        return (true, files);
    }
}
