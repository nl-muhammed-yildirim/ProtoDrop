namespace wa.application.UseCases.Billing;

/// <summary>US-019-07: narrow port for the webhook re-fetch (kept apart from IStripeCheckoutGateway).</summary>
public interface IStripeSubscriptionSource
{
    /// <summary>Fetches Stripe's current state of one subscription; Stripe is the source of truth when a webhook arrives out of order.</summary>
    /// <param name="subscriptionId">Stripe subscription id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The current snapshot, or <see langword="null"/> when Stripe answers 404 (unknown subscription).</returns>
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);
}
