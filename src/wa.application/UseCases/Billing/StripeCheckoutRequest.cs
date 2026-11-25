namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-7.5 step 1): everything Stripe needs to open a subscription Checkout Session.</summary>
/// <param name="UserId">Identifier of the user of the stripe checkout request.</param>
/// <param name="PriceId">Identifier of the price of the stripe checkout request.</param>
/// <param name="SuccessUrl">The success URL of the stripe checkout request.</param>
/// <param name="CancelUrl">The cancel URL of the stripe checkout request.</param>
public sealed record StripeCheckoutRequest(Guid UserId, string PriceId, string SuccessUrl, string CancelUrl);
