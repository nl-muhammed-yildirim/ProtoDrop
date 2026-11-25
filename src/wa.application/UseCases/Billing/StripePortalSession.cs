namespace wa.application.UseCases.Billing;

/// <summary>Immutable data carrier for stripe portal session.</summary>
/// <param name="SessionId">Identifier of the session of the stripe portal session.</param>
/// <param name="Url">The URL of the stripe portal session.</param>
public sealed record StripePortalSession(string SessionId, string Url);
