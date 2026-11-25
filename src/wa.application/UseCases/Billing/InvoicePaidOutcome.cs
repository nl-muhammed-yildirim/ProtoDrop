namespace wa.application.UseCases.Billing;

/// <summary>What happened when a paid invoice was applied to the payment-failed email sequence.</summary>
public enum InvoicePaidOutcome
{
    /// <summary>An open sequence existed and its remaining emails were cancelled.</summary>
    Cancelled,
    /// <summary>There was no open sequence for the subscription; nothing to cancel.</summary>
    NoSequence,
    /// <summary>The Stripe event was already processed.</summary>
    DuplicateEvent,
    /// <summary>The event raced the email pass; it is rejected as retryable so Stripe redelivers it.</summary>
    Conflict
}
