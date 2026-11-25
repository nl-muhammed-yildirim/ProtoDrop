namespace wa.application.UseCases.Billing;

/// <summary>Enumerates the stripe apply outcome values.</summary>
public enum StripeApplyOutcome
{
    /// <summary>Applied.</summary>
    Applied,
    /// <summary>Duplicate.</summary>
    Duplicate,
    /// <summary>User unresolved.</summary>
    UserUnresolved
}
