namespace wa.application.UseCases.Billing;

/// <summary>US-019-07: narrow port for the webhook re-fetch (kept apart from IStripeCheckoutGateway).</summary>
public interface IStripeSubscriptionSource
{
    /// <summary>Null means Stripe answered 404. Any other failure throws <see cref="BillingUnavailableException"/>.</summary>
    /// <param name="subscriptionId">Identifier of the subscription.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The stripe subscription snapshot, or <see langword="null"/> when there is none.</returns>
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);
}
