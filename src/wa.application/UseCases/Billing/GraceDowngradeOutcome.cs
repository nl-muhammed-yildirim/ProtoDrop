namespace wa.application.UseCases.Billing;

/// <summary>What happened when the grace downgrade was attempted for one subscription.</summary>
public enum GraceDowngradeOutcome
{
    /// <summary>The user was moved to the free plan.</summary>
    Downgraded,
    /// <summary>The locked re-read found that the subscription is no longer paused or its grace has not ended.</summary>
    NotDue,
    /// <summary>Another run already claimed this grace end, so nothing was done.</summary>
    AlreadyClaimed,
    /// <summary>The user is already on the free plan, so no change was made.</summary>
    NoChange
}
