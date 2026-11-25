using MediatR;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// GET /api/v1/public/transfers/{linkId} request (TA-4.2#4). Token = the optional ?t= unlock JWT (T-003-04, TA-9.6#1) —
/// default null so all existing call sites compile unchanged; only the Program.cs endpoint 5 wiring fills it.
/// </summary>
/// <param name="LinkId">Identifier of the link of the get public transfer query.</param>
/// <param name="Token">The token of the get public transfer query.</param>
public sealed record GetPublicTransferQuery(string LinkId, string? Token = null) : IRequest<GetPublicTransferResult>;
