namespace wa.domain;

/// <summary>Billing mirror of a Stripe subscription (P1). TA-3.2: dbo.Subscription.</summary>
public class Subscription
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    /// <summary>Stripe subscription ID (unique).</summary>
    public string StripeSubId { get; set; } = null!;
    public string? StripeCustomerId { get; set; }
    public string PlanCode { get; set; } = null!;
    /// <summary>active|trialing|past_due|paused|canceled.</summary>
    public string Status { get; set; } = null!;
    public DateTime? CurrentPeriodEndUtc { get; set; }
    public DateTime? GraceEndsAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
