namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-03 (TA-4.2#20 / FR-011-6): platform-wide overview aggregates — six bounded queries over WaDbContext.
/// Windows are passed IN (not computed in the store) so the UTC day boundary is unit-testable from wa.application.unit.
/// </summary>
public interface IOverviewStore
{
    /// <param name="todayStartUtc">UTC midnight of the current calendar day — for "new users today" count.</param>
    /// <param name="emailsWindowStartUtc">now − 7 days — for email sent/failed counts.</param>
    Task<OverviewData> GetAsync(DateTime todayStartUtc, DateTime emailsWindowStartUtc, CancellationToken ct);
}
