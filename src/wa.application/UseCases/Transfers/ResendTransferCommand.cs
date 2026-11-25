using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend request (endpoint 16, T-010-01). Cookie required — the session owner is passed in.</summary>
/// <param name="TransferId">Id of the original transfer, from the route.</param>
/// <param name="OwnerAppUserId">Id of the signed-in user; must be the owner of the original.</param>
public sealed record ResendTransferCommand(Guid TransferId, Guid? OwnerAppUserId = null) : IRequest<ResendTransferResult?>;
