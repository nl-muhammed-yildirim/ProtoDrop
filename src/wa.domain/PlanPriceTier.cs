using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>One billing interval's price: amount in minor units and the Stripe Price ID (null until the operator wires it).</summary>
public class PlanPriceTier
{
    /// <summary>Gets or sets the amount of the plan price tier.</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    /// <summary>Gets or sets the stripe price ID of the plan price tier.</summary>
    [JsonPropertyName("stripePriceId")]
    public string? StripePriceId { get; set; }

    /// <summary>False when no Stripe Price ID is set yet — checkout stays disabled (T-033).</summary>
    /// <returns><see langword="true"/> when wired to stripe; otherwise <see langword="false"/>.</returns>
    public bool IsWiredToStripe() => !string.IsNullOrWhiteSpace(StripePriceId);
}
