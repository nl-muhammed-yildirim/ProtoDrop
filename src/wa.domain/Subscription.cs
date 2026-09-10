namespace wa.domain;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid AppUserId { get; set; }
    public string StripeSubId { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndUtc { get; set; }
    public DateTime? GraceEndsAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}