namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-7.5 step 1): everything Stripe needs to open a subscription Checkout Session.</summary>
public sealed record StripeCheckoutRequest(Guid UserId, string PriceId, string SuccessUrl, string CancelUrl);

public sealed record StripeCheckoutSession(string SessionId, string Url);

public sealed record StripePortalSession(string SessionId, string Url);

/// <summary>US-019-01 (D-1): port over the Stripe REST API — the adapter lives in wa.infrastructure (no Stripe package).</summary>
public interface IStripeCheckoutGateway
{
    /// <summary>Creates a mode=subscription Checkout Session. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct);

    /// <summary>US-019-02: creates a Billing Portal session for an existing customer. Throws <see cref="BillingUnavailableException"/> on any Stripe/transport failure.</summary>
    Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);
}

/// <summary>US-019-01 (AC-019-5): Stripe unreachable or the plan price is not wired — maps to 503 BILLING_UNAVAILABLE; nothing was persisted.</summary>
public sealed class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
