namespace wa.application.UseCases.Billing;

/// <summary>Enumerates the payment failed outcome values.</summary>
public enum PaymentFailedOutcome
{
    /// <summary>Started.</summary>
    Started,
    /// <summary>Duplicate event.</summary>
    DuplicateEvent,
    /// <summary>Duplicate invoice.</summary>
    DuplicateInvoice,
    /// <summary>Ineligible.</summary>
    Ineligible,
    /// <summary>Subscription unresolved.</summary>
    SubscriptionUnresolved
}
