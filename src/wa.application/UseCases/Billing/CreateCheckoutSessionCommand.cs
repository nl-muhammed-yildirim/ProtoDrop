using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-4.2#18 / FR-019-1): open a Stripe Checkout Session for the session user. Persists nothing.</summary>
/// <param name="UserId">Identifier of the user of the create checkout session command.</param>
/// <param name="PlanCode">The plan code of the create checkout session command.</param>
/// <param name="Interval">The interval of the create checkout session command.</param>
public sealed record CreateCheckoutSessionCommand(Guid UserId, string PlanCode, BillingInterval Interval) : IRequest<StripeCheckoutSession>;
