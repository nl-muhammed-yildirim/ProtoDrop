namespace wa.application.UseCases.Billing;

/// <summary>What happened when a subscription change was applied to the billing mirror.</summary>
public enum StripeApplyOutcome
{
    /// <summary>The change was stored and, if requested, the user's plan was updated.</summary>
    Applied,
    /// <summary>The Stripe event was already processed; nothing changed.</summary>
    Duplicate,
    /// <summary>No user could be matched to the subscription; everything was rolled back so Stripe redelivers.</summary>
    UserUnresolved
}
