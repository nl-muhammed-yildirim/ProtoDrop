namespace wa.application.UseCases.Billing;

/// <summary>A created Stripe Billing Portal session.</summary>
/// <param name="SessionId">Stripe session id.</param>
/// <param name="Url">Hosted portal URL the browser is sent to.</param>
public sealed record StripePortalSession(string SessionId, string Url);
