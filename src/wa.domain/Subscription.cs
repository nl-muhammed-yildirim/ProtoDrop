namespace wa.domain;

/// <summary>Billing mirror of a Stripe subscription (P1). TA-3.2: dbo.Subscription.</summary>
public class Subscription
{
    /// <summary>Gets or sets the ID of the subscription.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the app user ID of the subscription.</summary>
    public Guid AppUserId { get; set; }
    /// <summary>Stripe subscription ID (unique).</summary>
    public string StripeSubId { get; set; } = null!;
    /// <summary>Gets or sets the stripe customer ID of the subscription.</summary>
    public string? StripeCustomerId { get; set; }
    /// <summary>Gets or sets the plan code of the subscription.</summary>
    public string PlanCode { get; set; } = null!;
    /// <summary>active|trialing|past_due|paused|canceled.</summary>
    public string Status { get; set; } = null!;
    /// <summary>Gets or sets the current period end UTC of the subscription.</summary>
    public DateTime? CurrentPeriodEndUtc { get; set; }
    /// <summary>Gets or sets the grace ends at UTC of the subscription.</summary>
    public DateTime? GraceEndsAtUtc { get; set; }
    /// <summary>Gets or sets the created at UTC of the subscription.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the updated at UTC of the subscription.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
