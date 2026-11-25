using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>One billing interval's price: amount in minor units and the Stripe Price ID (null until the operator wires it).</summary>
public class PlanPriceTier
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("stripePriceId")]
    public string? StripePriceId { get; set; }

    /// <summary>False when no Stripe Price ID is set yet — checkout stays disabled (T-033).</summary>
    public bool IsWiredToStripe() => !string.IsNullOrWhiteSpace(StripePriceId);
}
