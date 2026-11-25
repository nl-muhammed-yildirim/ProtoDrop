namespace wa.application.UseCases.Billing;

/// <summary>Enumerates the invoice paid outcome values.</summary>
public enum InvoicePaidOutcome
{
    /// <summary>Cancelled.</summary>
    Cancelled,
    /// <summary>No sequence.</summary>
    NoSequence,
    /// <summary>Duplicate event.</summary>
    DuplicateEvent,
    /// <summary>Conflict.</summary>
    Conflict
}
