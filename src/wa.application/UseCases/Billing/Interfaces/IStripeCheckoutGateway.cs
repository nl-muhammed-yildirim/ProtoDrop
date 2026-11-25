namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (D-1): port over the Stripe REST API — the adapter lives in wa.infrastructure (no Stripe package).</summary>
public interface IStripeCheckoutGateway
{
    /// <summary>Creates a mode=subscription Checkout Session. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct);

    /// <summary>US-019-02: creates a Billing Portal session for an existing customer. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);
}
