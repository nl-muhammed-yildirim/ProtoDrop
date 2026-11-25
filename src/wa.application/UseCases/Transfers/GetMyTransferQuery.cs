using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-03 (TA-4.2#15): GET /api/v1/transfers/{id} — the session user's transfer detail (row + files + recipients).</summary>
public sealed record GetMyTransferQuery(Guid Id, Guid OwnerId) : IRequest<GetMyTransferResult?>;
