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
    /// <param name="key">Flag key; must already exist as a seeded row.</param>
    /// <param name="value">New raw value, already validated against the key's type.</param>
    /// <param name="updatedAtUtc">UTC instant recorded as the change time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The row as it was before the update, or <see langword="null"/> when no row exists for <paramref name="key"/>.</returns>
    Task<FeatureFlag?> UpsertAsync(string key, string value, DateTime updatedAtUtc, CancellationToken ct);
}
