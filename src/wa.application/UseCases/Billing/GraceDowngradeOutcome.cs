namespace wa.application.UseCases.Billing;

/// <summary>Enumerates the grace downgrade outcome values.</summary>
public enum GraceDowngradeOutcome
{
    /// <summary>Downgraded.</summary>
    Downgraded,
    /// <summary>Not due.</summary>
    NotDue,
    /// <summary>Already claimed.</summary>
    AlreadyClaimed,
    /// <summary>No change.</summary>
    NoChange
}
