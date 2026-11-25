namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: house style — private ctor + static factories. Malformed → 400 VALIDATION; Retryable → 500 so Stripe redelivers (EC-019-1).</summary>
public sealed class StripeWebhookException : Exception
{
    private StripeWebhookException(string message, bool retryable) : base(message) => Retryable = retryable;

    /// <summary>True when the failure is transient and the endpoint should answer 500 so that Stripe redelivers the event.</summary>
    public bool Retryable { get; }

    /// <summary>The webhook body is not valid Stripe JSON; Stripe must not retry it, and the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>A non-retryable exception that the endpoint maps to 400 VALIDATION.</returns>
    public static StripeWebhookException ForMalformed() =>
        new("Stripe webhook payload is malformed.", retryable: false);

    /// <summary>The event cannot be applied yet (for example it raced another step); Stripe should redeliver it.</summary>
    /// <param name="message">Description that carries Stripe ids only, never PII.</param>
    /// <returns>A retryable exception that the endpoint maps to 500.</returns>
    public static StripeWebhookException ForRetryable(string message) => new(message, retryable: true);
}
