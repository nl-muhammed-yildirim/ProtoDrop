using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-004-01: thin adapter over WaDbContext implementing IZipFileSource — loads files by transfer Id for the f-zip function.
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class ZipFileSource(IDbContextFactory<WaDbContext> factory) : IZipFileSource
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<(bool Found, IReadOnlyList<PublicFileItem> Files)> GetFilesByTransferIdAsync(Guid transferId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var transfer = await db.Set<Transfer>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return (false, []);

        var files = await db.Set<FileItem>()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new PublicFileItem(f.Id, f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);
        return (true, files);
    }
}
