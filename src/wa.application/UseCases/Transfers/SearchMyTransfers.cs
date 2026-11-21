using MediatR;
using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (TA-4.2#14 + q): endpoint 14 with a q parameter present. Same result type as the plain list (single wire site).</summary>
public sealed record SearchMyTransfersQuery(Guid OwnerId, string? Q, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;

/// <summary>FR-045-5: feature.search off (or absent) → q is ignored entirely (plain list, plain cursor, no length check).
/// On → SearchTerm.Normalize (Q_TOO_LONG), search cursor bound to the term, ITransferSearchStore. One flag read per request.</summary>
public sealed class SearchMyTransfersHandler(ITransferStore transfers, ITransferSearchStore search, IFlagResolver flags)
    : IRequestHandler<SearchMyTransfersQuery, ListMyTransfersResult>
{
    public async Task<ListMyTransfersResult> Handle(SearchMyTransfersQuery request, CancellationToken cancellationToken)
    {
        var searchOn = await flags.ResolveFlag(FeatureGates.Search, FeatureGates.DefaultWhenAbsent, cancellationToken);
        var term = searchOn ? SearchTerm.Normalize(request.Q) : null;

        if (term is null)
        {
            DateTime? afterUtc = null;
            Guid? afterIdExcluded = null;
            if (!string.IsNullOrEmpty(request.Cursor))
                (afterUtc, afterIdExcluded) = CursorCodec.Decode(request.Cursor);
            var (items, nextCursor) = await transfers.ListByOwnerAsync(request.OwnerId, afterUtc, afterIdExcluded, request.Limit, request.Statuses, cancellationToken);
            return new ListMyTransfersResult(items, nextCursor);
        }

        int? afterRank = null;
        DateTime? searchAfterUtc = null;
        Guid? searchAfterId = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var decoded = SearchCursorCodec.Decode(request.Cursor);
            if (!string.Equals(decoded.Term, term, StringComparison.Ordinal))
                throw new CursorFormatException("cursor belongs to a different search.");
            afterRank = decoded.Rank;
            searchAfterUtc = decoded.CreatedAtUtc;
            searchAfterId = decoded.Id;
        }

        var (rows, next) = await search.SearchByOwnerAsync(request.OwnerId, term, request.Statuses, afterRank, searchAfterUtc, searchAfterId, request.Limit, cancellationToken);
        return new ListMyTransfersResult(rows, next);
    }
}
