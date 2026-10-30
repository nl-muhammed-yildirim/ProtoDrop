using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's transfers, cursor-paged, most recent first.</summary>
public sealed record ListMyTransfersQuery(Guid OwnerId, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;

/// <summary>One row of the My Files history list (US-009-01 happy-path payload — no extra round-trips per row).</summary>
public sealed record MyTransferRow(
    Guid Id,
    string Name,
    int FileCount,
    long TotalBytes,
    byte Status,
    DateTime ExpiresAtUtc,
    int DownloadsCount,
    int MaxDownloads,
    int RecipientCount);

/// <summary>Page result: the rows plus the opaque next cursor (null = last page).</summary>
public sealed record ListMyTransfersResult(IReadOnlyList<MyTransferRow> Items, string? NextCursor);

/// <summary>
/// T-009-01 (TA-4.1.4 pin): decodes the cursor (base64("{createdAtUtc:O}:{id:N}")), asks the store for the next page,
/// and returns rows + NextCursor exactly as the store produced them (the store encodes it from the last row).
/// Malformed cursor → CursorFormatException propagates to the endpoint → 400 VALIDATION (existing closed-list member).
/// </summary>
public sealed class ListMyTransfersHandler : IRequestHandler<ListMyTransfersQuery, ListMyTransfersResult>
{
    private readonly ITransferStore _transfers;

    public ListMyTransfersHandler(ITransferStore transfers)
    {
        _transfers = transfers;
    }

    public async Task<ListMyTransfersResult> Handle(ListMyTransfersQuery request, CancellationToken cancellationToken)
    {
        DateTime? afterUtc = null;
        Guid? afterIdExcluded = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            (afterUtc, afterIdExcluded) = CursorCodec.Decode(request.Cursor);
        }

        var (items, nextCursor) = await _transfers.ListByOwnerAsync(request.OwnerId, afterUtc, afterIdExcluded, request.Limit, request.Statuses, cancellationToken);
        return new ListMyTransfersResult(items, nextCursor);
    }
}
