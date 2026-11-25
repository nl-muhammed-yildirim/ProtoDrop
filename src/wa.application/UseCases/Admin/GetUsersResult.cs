using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: flat result — items + nextCursor (null when no next page).</summary>
/// <param name="Items">User rows of this page, newest first.</param>
/// <param name="NextCursor">Cursor for the next page; null when this is the last page.</param>
public record GetUsersResult(
    IReadOnlyList<AdminUserRow> Items,
    string? NextCursor);
