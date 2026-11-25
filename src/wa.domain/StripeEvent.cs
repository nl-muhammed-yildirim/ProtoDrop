namespace wa.domain;

/// <summary>Webhook idempotency record. TA-3.2: dbo.StripeEvent.</summary>
public class StripeEvent
{
    /// <summary>Stripe event ID — primary key (VARCHAR(100)).</summary>
    public string StripeEventId { get; set; } = null!;
    /// <summary>UTC instant the webhook event was handled; its presence makes a Stripe redelivery a no-op.</summary>
    public DateTime ProcessedAtUtc { get; set; }
}
