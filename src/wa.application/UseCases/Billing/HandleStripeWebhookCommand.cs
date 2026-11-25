using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-03 (TA-4.2a / FR-019-4): apply one signature-verified Stripe webhook body exactly once.
/// The endpoint verifies Stripe-Signature (it owns config + raw body); this command carries the verified raw payload.
/// </summary>
/// <param name="Payload">The payload of the handle stripe webhook command.</param>
public sealed record HandleStripeWebhookCommand(string Payload) : IRequest<StripeWebhookOutcome>;
