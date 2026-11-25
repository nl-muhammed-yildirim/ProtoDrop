namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (D-1): port over the Stripe REST API — the adapter lives in wa.infrastructure (no Stripe package).</summary>
public interface IStripeCheckoutGateway
{
    /// <summary>Creates a mode=subscription Checkout Session. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    /// <param name="request">User, price and return URLs of the checkout.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The created session with the hosted checkout URL.</returns>
    Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct);

    /// <summary>US-019-02: creates a Billing Portal session for an existing customer. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    /// <param name="customerId">Stripe customer id of the user.</param>
    /// <param name="returnUrl">URL the portal sends the user back to.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The created portal session with its URL.</returns>
    Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);
}
