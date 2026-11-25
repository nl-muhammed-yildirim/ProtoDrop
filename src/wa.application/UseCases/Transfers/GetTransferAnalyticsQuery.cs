using MediatR;
using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>US-023-01 (TA-4.2#24, FR-023-1/5): owner-scoped download analytics for one transfer. Null = not found (404).</summary>
/// <param name="Id">Identifier of the get transfer analytics query.</param>
/// <param name="OwnerId">Identifier of the owner of the get transfer analytics query.</param>
public sealed record GetTransferAnalyticsQuery(Guid Id, Guid OwnerId) : IRequest<TransferAnalytics?>;
