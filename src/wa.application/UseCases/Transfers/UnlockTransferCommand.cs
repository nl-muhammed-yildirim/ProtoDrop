using MediatR;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/public/transfers/{linkId}/unlock request (TA-4.2#5). Null Password = missing body/field → the handler throws the VALIDATION application-level exception (D1).</summary>
/// <param name="LinkId">Identifier of the link of the unlock transfer command.</param>
/// <param name="Password">The password of the unlock transfer command.</param>
public sealed record UnlockTransferCommand(string LinkId, string? Password) : IRequest<UnlockTransferResult>;
