using MediatR;
using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>Handles <see cref="GetErasuresQuery"/>: decodes the cursor, reads one page of erasure rows, masks emails unless revealed and derives the pending or swept status.</summary>
/// <param name="store">Read store for the erasure report.</param>
public sealed class GetErasuresQueryHandler(IErasureReportStore store) : IRequestHandler<GetErasuresQuery, GetErasuresResult>
{
    /// <inheritdoc />
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
