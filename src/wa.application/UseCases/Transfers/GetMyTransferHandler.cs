using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-009-03 (TA-4.2a thin endpoint): calls ITransferStore.GetByOwnerAsync; returns null when the store returns null —
/// handler stays pure (no BusinessException in application layer; mirrors SendTransfer's IRequest&lt;...?&gt; + endpoint-maps-null idiom).
/// </summary>
public sealed class GetMyTransferHandler : IRequestHandler<GetMyTransferQuery, GetMyTransferResult?>
{
    private readonly ITransferStore _transfers;

    /// <summary>Initializes a new instance of the <see cref="GetMyTransferHandler"/> class.</summary>
    /// <param name="transfers">Store that reads the owner's transfer with files and recipients.</param>
    public GetMyTransferHandler(ITransferStore transfers)
    {
        _transfers = transfers;
    }

    /// <inheritdoc />
    public async Task<GetMyTransferResult?> Handle(GetMyTransferQuery request, CancellationToken cancellationToken) =>
        await _transfers.GetByOwnerAsync(request.OwnerId, request.Id, cancellationToken);
}
