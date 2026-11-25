namespace wa.application.UseCases.Billing;

/// <summary>US-019-07: narrow port for the webhook re-fetch (kept apart from IStripeCheckoutGateway).</summary>
public interface IStripeSubscriptionSource
{
    /// <summary>Null means Stripe answered 404. Any other failure throws <see cref="BillingUnavailableException"/>.</summary>
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);
}
