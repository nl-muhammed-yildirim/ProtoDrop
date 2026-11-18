using MediatR;
using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-047-03 (FR-047-6, TA-4.1.4): GET /api/v1/admin/erasures — the DPO erasure report, cursor-paged.
/// RevealEmail is true for SuperAdmin (the DPO); Operator gets the masked form (TA-9.4).
/// </summary>
public sealed record GetErasuresQuery(string? Cursor, int Limit, bool RevealEmail) : IRequest<GetErasuresResult>;

public sealed record ErasureListItem(
    Guid UserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    string SweepStatus,
    DateTime? SweptAtUtc);

public sealed record GetErasuresResult(IReadOnlyList<ErasureListItem> Items, string? NextCursor);

/// <summary>US-047-03: page size is the TA-4.1.4 API pin (not a plan limit), as GdprSweepPolicy is for the sweep.</summary>
public static class ErasureReportPolicy
{
    public const int PageSize = 25;
    public const string StatusPending = "pending";
    public const string StatusSwept = "swept";
}

public sealed class GetErasuresQueryHandler(IErasureReportStore store) : IRequestHandler<GetErasuresQuery, GetErasuresResult>
{
    public async Task<GetErasuresResult> Handle(GetErasuresQuery request, CancellationToken cancellationToken)
    {
        // Malformed cursor throws CursorFormatException; the endpoint maps it to 400 VALIDATION.
        DateTime? afterUtc = null;
        Guid? afterId = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var (at, id) = CursorCodec.Decode(request.Cursor);
            afterUtc = at;
            afterId = id;
        }

        var (rows, nextCursor) = await store.GetPageAsync(afterUtc, afterId, request.Limit, cancellationToken);

        var items = rows.Select(r => new ErasureListItem(
            r.AppUserId,
            request.RevealEmail ? r.Email : EmailMask.Mask(r.Email),
            r.DeletedAtUtc,
            r.ReHomedTransferCount,
            // EC-047-4: pending until the sweep's MarkSwept succeeds — there is no other state.
            r.SweptAtUtc is null ? ErasureReportPolicy.StatusPending : ErasureReportPolicy.StatusSwept,
            r.SweptAtUtc)).ToList();

        return new GetErasuresResult(items, nextCursor);
    }
}
