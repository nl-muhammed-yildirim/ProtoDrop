using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: flat result — items + nextCursor (null when no next page).</summary>
/// <param name="Items">The items of the get users result.</param>
/// <param name="NextCursor">The next cursor of the get users result.</param>
public record GetUsersResult(
    IReadOnlyList<AdminUserRow> Items,
    string? NextCursor);
