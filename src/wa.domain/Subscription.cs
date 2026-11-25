namespace wa.domain;

/// <summary>Billing mirror of a Stripe subscription (P1). TA-3.2: dbo.Subscription.</summary>
public class Subscription
{
    /// <summary>Primary key of the subscription mirror row.</summary>
    public Guid Id { get; set; }
    /// <summary>The subscribing user.</summary>
    public Guid AppUserId { get; set; }
    /// <summary>Stripe subscription ID (unique).</summary>
    public string StripeSubId { get; set; } = null!;
    /// <summary>Stripe customer id of the user, used to open the billing portal; null until known.</summary>
    public string? StripeCustomerId { get; set; }
    /// <summary>Code of the plan the subscription pays for.</summary>
    public string PlanCode { get; set; } = null!;
    /// <summary>active|trialing|past_due|paused|canceled.</summary>
    public string Status { get; set; } = null!;
    /// <summary>UTC end of the current paid period as reported by Stripe; null before the first invoice.</summary>
    public DateTime? CurrentPeriodEndUtc { get; set; }
    /// <summary>UTC end of the payment grace period after a failed renewal; null when not in grace.</summary>
    public DateTime? GraceEndsAtUtc { get; set; }
    /// <summary>UTC instant the mirror row was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>UTC instant the mirror row was last synced from Stripe.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
