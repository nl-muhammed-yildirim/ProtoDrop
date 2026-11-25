using MediatR;
using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>US-023-01 (TA-4.2#24, FR-023-1/5): owner-scoped download analytics for one transfer. Null = not found (404).</summary>
/// <param name="Id">Id of the transfer, from the route.</param>
/// <param name="OwnerId">Id of the signed-in user; only the owner may read analytics.</param>
public sealed record GetTransferAnalyticsQuery(Guid Id, Guid OwnerId) : IRequest<TransferAnalytics?>;
