using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>One billing interval's price: amount in minor units and the Stripe Price ID (null until the operator wires it).</summary>
public class PlanPriceTier
{
    /// <summary>Price for one billing period in minor currency units (for example cents).</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    /// <summary>Stripe Price object id used at checkout; null until the operator wires it.</summary>
    [JsonPropertyName("stripePriceId")]
    public string? StripePriceId { get; set; }

    /// <summary>False when no Stripe Price ID is set yet — checkout stays disabled (T-033).</summary>
    /// <returns><see langword="true"/> when a non-blank <see cref="StripePriceId"/> is set; otherwise <see langword="false"/>.</returns>
    public bool IsWiredToStripe() => !string.IsNullOrWhiteSpace(StripePriceId);
}
