using MediatR;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-4.2#18 / FR-019-1): open a Stripe Checkout Session for the session user. Persists nothing.</summary>
/// <param name="UserId">Id of the signed-in user who upgrades.</param>
/// <param name="PlanCode">Code of the paid plan to subscribe to.</param>
/// <param name="Interval">Monthly or annual billing.</param>
public sealed record CreateCheckoutSessionCommand(Guid UserId, string PlanCode, BillingInterval Interval) : IRequest<StripeCheckoutSession>;
