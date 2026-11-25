using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend request (endpoint 16, T-010-01). Cookie required — the session owner is passed in.</summary>
public sealed record ResendTransferCommand(Guid TransferId, Guid? OwnerAppUserId = null) : IRequest<ResendTransferResult?>;
