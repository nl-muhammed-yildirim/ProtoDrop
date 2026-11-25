namespace wa.application.UseCases.EmailSettings;

/// <summary>F-XCT-003 (FR-046-1/2/3, TA-2.3): narrow port for AppUser.EmailNotificationsEnabled — separate from IAppUserStore (IOrgNameStore precedent).</summary>
public interface IEmailNotificationsStore
{
    /// <summary>Store the explicit choice for a non-deleted user. A missing row is a no-op.</summary>
    /// <param name="userId">The account to update.</param>
    /// <param name="enabled">True to receive owner notification emails; false to opt out.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SetAsync(Guid userId, bool enabled, CancellationToken ct);

    /// <summary>Stored value of a non-deleted user; null when the column is null OR the row is unknown/deleted (both mean on).</summary>
    /// <param name="userId">The account to read.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The stored choice, or <see langword="null"/> when the user never chose or does not exist (both count as on).</returns>
    Task<bool?> GetAsync(Guid userId, CancellationToken ct);
}
