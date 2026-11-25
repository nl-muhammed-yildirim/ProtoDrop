namespace wa.application.UseCases.Billing;

/// <summary>What happened when a payment-failed invoice event was applied.</summary>
public enum PaymentFailedOutcome
{
    /// <summary>The sequence was started and email #1 is due.</summary>
    Started,
    /// <summary>The Stripe event was already processed.</summary>
    DuplicateEvent,
    /// <summary>Another event of the same invoice already started the sequence.</summary>
    DuplicateInvoice,
    /// <summary>The subscription status (canceled, incomplete, unpaid or paused) gets no sequence.</summary>
    Ineligible,
    /// <summary>No subscription mirror row exists yet; the event is retried on redelivery.</summary>
    SubscriptionUnresolved
}
