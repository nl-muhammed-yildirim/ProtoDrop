namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-7.5 step 1): everything Stripe needs to open a subscription Checkout Session.</summary>
/// <param name="UserId">The subscribing user; stored as metadata so the webhook can match the subscription.</param>
/// <param name="PriceId">Stripe Price object id of the chosen plan and interval.</param>
/// <param name="SuccessUrl">URL Stripe redirects to after a successful payment.</param>
/// <param name="CancelUrl">URL Stripe redirects to when the user abandons checkout.</param>
public sealed record StripeCheckoutRequest(Guid UserId, string PriceId, string SuccessUrl, string CancelUrl);
