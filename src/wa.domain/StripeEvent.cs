namespace wa.domain;

public class StripeEvent
{
    public string StripeEventId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
