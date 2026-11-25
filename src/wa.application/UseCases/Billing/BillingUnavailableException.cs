namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (AC-019-5): Stripe unreachable or the plan price is not wired — maps to 503 BILLING_UNAVAILABLE; nothing was persisted.</summary>
public sealed class BillingUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BillingUnavailableException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner.</param>
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
