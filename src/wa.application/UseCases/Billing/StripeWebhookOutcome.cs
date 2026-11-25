namespace wa.application.UseCases.Billing;

/// <summary>What happened to a verified Stripe webhook event; every outcome answers 2xx.</summary>
public enum StripeWebhookOutcome
{
    /// <summary>The event changed state or queued emails.</summary>
    Applied,
    /// <summary>The event or invoice was already processed.</summary>
    Duplicate,
    /// <summary>The event type or subscription state is not relevant and was acknowledged without change.</summary>
    Ignored
}
