using MediatR;

namespace wa.application.UseCases.Transfers;

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
