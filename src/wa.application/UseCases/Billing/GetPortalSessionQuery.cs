using MediatR;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-02 (TA-4.2#18): open a Stripe Billing Portal session for the session user's customer. Persists nothing.</summary>
public sealed record GetPortalSessionQuery(Guid UserId) : IRequest<PortalSessionResult>;
