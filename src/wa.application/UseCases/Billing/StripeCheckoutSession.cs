namespace wa.application.UseCases.Billing;

public sealed record StripeCheckoutSession(string SessionId, string Url);
