using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): GET /api/v1/admin/users — admin users list, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim + lowercase, cursor decode, then one store call.
/// </summary>
/// <param name="Q">Optional email search text; trimmed and lowercased by the handler.</param>
/// <param name="Cursor">Opaque cursor from the previous page; null or empty for the first page.</param>
/// <param name="Limit">Maximum number of rows to return.</param>
public sealed record GetUsersQuery(
    string? Q,
    string? Cursor,
    int Limit) : IRequest<GetUsersResult>;
