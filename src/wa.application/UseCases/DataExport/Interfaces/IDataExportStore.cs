namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01 (FR-047-1/2): API-side DataExport surface (TA-2.3 dependency inversion). Adapter in wa.infrastructure.Persistence.</summary>
public interface IDataExportStore
{
    /// <summary>The user's most recent export row (by CreatedAtUtc), or null when none.</summary>
    /// <param name="userId">The account whose export is wanted.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The newest export row, or <see langword="null"/> when the user never requested one.</returns>
    Task<wa.domain.DataExport?> GetLatestAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts the row; false on a unique violation (2627 / 2601) = another processing export already exists (EC-047-6).</summary>
    /// <param name="row">The new processing export row.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the row was inserted; <see langword="false"/> when another processing export already exists.</returns>
    Task<bool> TryCreateAsync(wa.domain.DataExport row, CancellationToken ct);
}
