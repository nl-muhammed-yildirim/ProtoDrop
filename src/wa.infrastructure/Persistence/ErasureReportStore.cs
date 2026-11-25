using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Admin;
using wa.domain;
using wa.domain.Transfers;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-047-03 (FR-047-6): read adapter for the erasure report. A fresh context per call (DataExportStore precedent).
/// One row per account by construction (Erasure PK = AppUserId, EC-047-3); email comes from a correlated AppUser lookup
/// and is null once the user row is hard-deleted (no FK by design).
/// </summary>
public sealed class ErasureReportStore : IErasureReportStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="ErasureReportStore"/> class.</summary>
    /// <param name="factory">The factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public ErasureReportStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
        DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.Set<Erasure>()
            .AsNoTracking()
            .Where(e => afterUtc == null || e.DeletedAtUtc < afterUtc.Value || (e.DeletedAtUtc == afterUtc.Value && e.AppUserId < afterIdExcluded!.Value))
            .OrderByDescending(e => e.DeletedAtUtc)
            .ThenByDescending(e => e.AppUserId)
            .Take(limit + 1)
            .Select(e => new ErasureReportRow(
                e.AppUserId,
                db.Set<AppUser>().Where(u => u.Id == e.AppUserId).Select(u => (string?)u.Email).FirstOrDefault(),
                e.DeletedAtUtc,
                e.ReHomedTransferCount,
                e.SweptAtUtc))
            .ToListAsync(ct);

        var nextCursor = rows.Count > limit
            ? CursorCodec.Encode(rows[limit - 1].DeletedAtUtc, rows[limit - 1].AppUserId)
            : null;

        return (rows.Take(limit).ToList(), nextCursor);
    }
}
