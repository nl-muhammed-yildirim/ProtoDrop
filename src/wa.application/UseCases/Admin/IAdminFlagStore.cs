using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): admin flag write surface — single-key PK upsert over WaDbContext.
/// Returns the OLD FeatureFlag row (pre-update) or null when no row for that key (unknown key → handler 404).
/// </summary>
public interface IAdminFlagStore
{
    /// <summary>
    /// Upsert a flag's value by its Key (single-key PK, last-write-wins — EC-011-1).
    /// Returns the pre-update row or null when no row exists for that key.
    /// </summary>
    Task<FeatureFlag?> UpsertAsync(string key, string value, DateTime updatedAtUtc, CancellationToken ct);
}
