using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// GET /api/v1/public/transfers/{linkId} request (TA-4.2#4). Token = the optional ?t= unlock JWT (T-003-04, TA-9.6#1) —
/// default null so all existing call sites compile unchanged; only the Program.cs endpoint 5 wiring fills it.
/// </summary>
/// <param name="LinkId">Public 8-character link id from the route.</param>
/// <param name="Token">Unlock token from the <c>t</c> query parameter; null when absent.</param>
public sealed record GetPublicTransferQuery(string LinkId, string? Token = null) : IRequest<GetPublicTransferResult>;
