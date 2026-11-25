namespace wa.application.UseCases.Billing;

/// <summary>A created Stripe Checkout Session.</summary>
/// <param name="SessionId">Stripe session id.</param>
/// <param name="Url">Hosted checkout URL the browser is sent to.</param>
public sealed record StripeCheckoutSession(string SessionId, string Url);
