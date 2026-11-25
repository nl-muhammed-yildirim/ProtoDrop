namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1/2): API-side DataExport surface (TA-2.3 dependency inversion). Adapter in wa.infrastructure.Persistence.</summary>
public interface IDataExportStore
{
    /// <summary>The user's most recent export row (by CreatedAtUtc), or null when none.</summary>
    Task<wa.domain.DataExport?> GetLatestAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts the row; false on a unique violation (2627 / 2601) = another processing export already exists (EC-047-6).</summary>
    Task<bool> TryCreateAsync(wa.domain.DataExport row, CancellationToken ct);
}
