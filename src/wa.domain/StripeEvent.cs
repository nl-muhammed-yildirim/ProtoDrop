namespace wa.domain;

/// <summary>Webhook idempotency record. TA-3.2: dbo.StripeEvent.</summary>
public class StripeEvent
{
    /// <summary>Stripe event ID — primary key (VARCHAR(100)).</summary>
    public string StripeEventId { get; set; } = null!;
    /// <summary>Gets or sets the processed at UTC of the stripe event.</summary>
    public DateTime ProcessedAtUtc { get; set; }
}
