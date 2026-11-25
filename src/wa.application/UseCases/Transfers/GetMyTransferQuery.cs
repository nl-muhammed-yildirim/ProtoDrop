using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-03 (TA-4.2#15): GET /api/v1/transfers/{id} — the session user's transfer detail (row + files + recipients).</summary>
/// <param name="Id">Id of the transfer, from the route.</param>
/// <param name="OwnerId">Id of the signed-in user; only a transfer owned by this user is returned.</param>
public sealed record GetMyTransferQuery(Guid Id, Guid OwnerId) : IRequest<GetMyTransferResult?>;
