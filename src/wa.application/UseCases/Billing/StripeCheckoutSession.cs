namespace wa.application.UseCases.Billing;

/// <summary>Immutable data carrier for stripe checkout session.</summary>
/// <param name="SessionId">Identifier of the session of the stripe checkout session.</param>
/// <param name="Url">The URL of the stripe checkout session.</param>
public sealed record StripeCheckoutSession(string SessionId, string Url);
