using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-06 (TA-4.2#22 / FR-011-2): GET /api/v1/admin/users — admin users list, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim + lowercase, cursor decode, then one store call.
/// </summary>
public sealed record GetUsersQuery(
    string? Q,
    string? Cursor,
    int Limit) : IRequest<GetUsersResult>;

/// <summary>T-011-06: flat result — items + nextCursor (null when no next page).</summary>
public record GetUsersResult(
    IReadOnlyList<AdminUserRow> Items,
    string? NextCursor);

/// <summary>
/// T-011-06: handler — pure composition over IAdminUserStore.
/// (1) q = Q?.Trim(); null/empty → null; else q.ToLowerInvariant() (AppUser.Email is stored lowercased —
///      an uppercase q would miss without normalization);
/// (2) cursor decoded via CursorCodec.Decode (malformed → rethrow CursorFormatException; endpoint maps to 400 VALIDATION);
/// (3) call store once with the resolved args + limit.
/// </summary>
public sealed class GetUsersQueryHandler(IAdminUserStore store) : IRequestHandler<GetUsersQuery, GetUsersResult>
{
    public async Task<GetUsersResult> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        // (1) q: trim and null out empty/whitespace-only; lowercase (stored-lowercase column).
        var q = request.Q?.Trim();
        if (!string.IsNullOrEmpty(q))
            q = q.ToLowerInvariant();
        else
            q = null;

        // (2) cursor decode — malformed → CursorFormatException (endpoint maps to 400 VALIDATION).
        DateTime? afterUtc = null;
        Guid? afterIdExcluded = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var (createdAtUtc, id) = CursorCodec.Decode(request.Cursor);
            afterUtc = createdAtUtc;
            afterIdExcluded = id;
        }

        // (3) call store once with resolved args + limit.
        var (items, nextCursor) = await store.GetUsersAsync(
            q,
            afterUtc,
            afterIdExcluded,
            request.Limit,
            cancellationToken);

        return new GetUsersResult(items, nextCursor);
    }
}
