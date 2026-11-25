using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): GET /api/v1/admin/users — admin users list, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim + lowercase, cursor decode, then one store call.
/// </summary>
/// <param name="Q">The q of the get users query.</param>
/// <param name="Cursor">The cursor of the get users query.</param>
/// <param name="Limit">The limit of the get users query.</param>
public sealed record GetUsersQuery(
    string? Q,
    string? Cursor,
    int Limit) : IRequest<GetUsersResult>;
