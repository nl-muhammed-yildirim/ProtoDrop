using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#24 / FR-011-2): admin suppressions surface — list + remove over WaDbContext.
/// Mirrors IAdminUserStore's mixed read+write shape (GetUsersAsync + SetPlanIdAsync).
/// </summary>
public interface IAdminSuppressionStore
{
    /// <summary>List all suppression rows (no order contract — the handler orders CreatedAtUtc descending).</summary>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching email suppression items.</returns>
    Task<IReadOnlyList<EmailSuppression>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Remove a suppression row by Id — returns the REMOVED row or null when no row for that Id.
    /// Null → handler passes through → endpoint 404 NOT_FOUND; returning the row lets the endpoint
    /// audit { email } from it (FR-011-5).
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The email suppression, or <see langword="null"/> when there is none.</returns>
    Task<EmailSuppression?> RemoveByIdAsync(Guid id, CancellationToken ct);
}
