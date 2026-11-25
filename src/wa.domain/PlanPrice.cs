using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// Shape contract and read model for <see cref="Plan.PriceJson"/> (F-BIL-001, FR-018-2/3). Amounts are minor units.
/// <see cref="PlanDefinition"/> uses the JsonPropertyName set as the exact key contract. Checkout (T-033) and the plan
/// screen (T-036) read prices only through <see cref="Read"/>; no price is ever computed in app code.
/// Computed helpers must stay methods: PlanDefinition derives required keys from public instance properties.
/// </summary>
public class PlanPrice
{
    /// <summary>ISO 4217 code; single currency per row.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>Gets or sets the monthly of the plan price.</summary>
    [JsonPropertyName("monthly")]
    public PlanPriceTier Monthly { get; set; } = null!;

    /// <summary>Gets or sets the annual of the plan price.</summary>
    [JsonPropertyName("annual")]
    public PlanPriceTier Annual { get; set; } = null!;

    /// <summary>null = not seat-priced; set = monthly price per seat in minor units, seats = users (FR-018-3). Checkout bills it as Stripe quantity (T-033).</summary>
    [JsonPropertyName("perSeatMonthly")]
    public long? PerSeatMonthly { get; set; }

    /// <summary>Parses the row's PriceJson; throws <see cref="InvalidOperationException"/> naming the plan code when it yields no value.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The plan price.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the operation is not valid in the current state.
    /// </exception>
    public static PlanPrice Read(Plan plan)
    {
        var price = JsonSerializer.Deserialize<PlanPrice>(plan.PriceJson);
        return price ?? throw new InvalidOperationException($"Plan '{plan.Code}' has no price data.");
    }

    /// <summary>Creates for the plan price tier.</summary>
    /// <param name="interval">The interval.</param>
    /// <returns>The plan price tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an argument is outside the allowed range.</exception>
    public PlanPriceTier For(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => Monthly,
        BillingInterval.Annual => Annual,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
    };
}
