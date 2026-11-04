namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): admin users list — one bounded query over WaDbContext.
/// All filter args are passed IN (not parsed in the store) so handler composition is unit-testable from
/// wa.application.unit without EF — mirrors ISearchTransfersStore's windows-in style.
/// </summary>
public interface IAdminUserStore
{
    /// <param name="q">Trimmed + lowercased search term (null = no q predicate). Matches AppUser.Email substring.</param>
    /// <param name="afterUtc">Cursor: rows with CreatedAtUtc &lt; afterUtc (null = first page).</param>
    /// <param name="afterIdExcluded">Cursor: rows with CreatedAtUtc == afterUtc AND Id &lt; afterIdExcluded (null = no tiebreak).</param>
    /// <param name="limit">Page size (TA-4.1.4: 25 max).</param>
    Task<(List<AdminUserRow> Items, string? NextCursor)> GetUsersAsync(
        string? q,
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}

/// <summary>T-011-06: one row in the admin users list (TA-4.2#22). Storage + count are computed, not stored.</summary>
public record AdminUserRow(
    Guid Id,
    string Email,
    string? PlanName,
    long StorageBytesUsed,
    int ActiveTransferCount,
    DateTime CreatedAtUtc);
