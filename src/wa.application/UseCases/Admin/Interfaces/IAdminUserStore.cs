namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): admin users list — one bounded query over WaDbContext.
/// All filter args are passed IN (not parsed in the store) so handler composition is unit-testable from
/// wa.application.unit without EF — mirrors ISearchTransfersStore's windows-in style.
/// </summary>
public interface IAdminUserStore
{
    /// <summary>Reads one page of users, newest first, with their computed storage and active-transfer figures.</summary>
    /// <param name="q">Trimmed + lowercased search term (null = no q predicate). Matches AppUser.Email substring.</param>
    /// <param name="afterUtc">Cursor: rows with CreatedAtUtc &lt; afterUtc (null = first page).</param>
    /// <param name="afterIdExcluded">Cursor: rows with CreatedAtUtc == afterUtc AND Id &lt; afterIdExcluded (null = no tiebreak).</param>
    /// <param name="limit">Page size (TA-4.1.4: 25 max).</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The page of rows and the cursor of the next page (null when there is none).</returns>
    Task<(List<AdminUserRow> Items, string? NextCursor)> GetUsersAsync(
        string? q,
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// T-011-07 (TA-4.2#22 / FR-011-2): set AppUser.PlanId = newPlanId WHERE Id == userId — returns false when the row
    /// vanished in a race (mirror SoftDeleteWithReHomeAsync's bool contract). Limits take effect at the NEXT finalize
    /// (FR-BIL-001-4 "applies to new transfers") — no limit recompute here.
    /// </summary>
    /// <param name="userId">Id of the user whose plan is changed.</param>
    /// <param name="newPlanId">Id of the plan to assign.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the user was updated; <see langword="false"/> when the user row no longer exists.</returns>
    Task<bool> SetPlanIdAsync(Guid userId, Guid newPlanId, CancellationToken ct);
}
