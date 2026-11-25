using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.DataExport;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-047-01: API-side adapter for <see cref="IDataExportStore"/>. A fresh WaDbContext per call, so a failed INSERT
/// (unique violation) never stays tracked on a shared context (BillingSequenceStore precedent).
/// </summary>
public sealed class DataExportStore : IDataExportStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="DataExportStore"/> class.</summary>
    /// <param name="factory">Factory that creates a short-lived database context per call.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public DataExportStore(IDbContextFactory<WaDbContext> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <inheritdoc />
    public async Task<DataExport?> GetLatestAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<DataExport>().AsNoTracking()
            .Where(e => e.AppUserId == userId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(DataExport row, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<DataExport>().Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            return false; // UX_DataExport_InFlight: another processing export exists
        }
    }
}
