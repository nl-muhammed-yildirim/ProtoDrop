using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-03 (TA-4.2#15): GET /api/v1/transfers/{id} — the session user's transfer detail (row + files + recipients).</summary>
public sealed record GetMyTransferQuery(Guid Id, Guid OwnerId) : IRequest<GetMyTransferResult?>;

/// <summary>One per-file row of the My Files detail (FR-009-1: name + size only — file ids stay internal at MVP).</summary>
public sealed record GetMyTransferFile(string Name, long SizeBytes);

/// <summary>
/// T-009-03: the owner-detail payload. COMPOSES T-009-01's MyTransferRow (single source of truth for the row fields —
/// no duplicated flat copy) and adds only the per-file rows + recipient addresses. Null from the handler = not found
/// / foreign owner / guest / deleted — ONE indistinguishable case (endpoint maps null → 404 NOT_FOUND; no FORBIDDEN).
/// </summary>
public sealed record GetMyTransferResult(MyTransferRow Row, IReadOnlyList<GetMyTransferFile> Files, IReadOnlyList<string> Recipients);

/// <summary>
/// T-009-03 (TA-4.2a thin endpoint): calls ITransferStore.GetByOwnerAsync; returns null when the store returns null —
/// handler stays pure (no BusinessException in application layer; mirrors SendTransfer's IRequest<...?> + endpoint-maps-null idiom).
/// </summary>
public sealed class GetMyTransferHandler : IRequestHandler<GetMyTransferQuery, GetMyTransferResult?>
{
    private readonly ITransferStore _transfers;

    public GetMyTransferHandler(ITransferStore transfers)
    {
        _transfers = transfers;
    }

    public async Task<GetMyTransferResult?> Handle(GetMyTransferQuery request, CancellationToken cancellationToken) =>
        await _transfers.GetByOwnerAsync(request.OwnerId, request.Id, cancellationToken);
}
