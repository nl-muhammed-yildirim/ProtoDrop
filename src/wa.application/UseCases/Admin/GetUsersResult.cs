using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: flat result — items + nextCursor (null when no next page).</summary>
public record GetUsersResult(
    IReadOnlyList<AdminUserRow> Items,
    string? NextCursor);
