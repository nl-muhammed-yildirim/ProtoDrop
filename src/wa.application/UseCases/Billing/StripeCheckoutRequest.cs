namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-7.5 step 1): everything Stripe needs to open a subscription Checkout Session.</summary>
public sealed record StripeCheckoutRequest(Guid UserId, string PriceId, string SuccessUrl, string CancelUrl);
