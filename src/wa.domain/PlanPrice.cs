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

    /// <summary>Price of the monthly billing interval.</summary>
    [JsonPropertyName("monthly")]
    public PlanPriceTier Monthly { get; set; } = null!;

    /// <summary>Price of the annual billing interval.</summary>
    [JsonPropertyName("annual")]
    public PlanPriceTier Annual { get; set; } = null!;

    /// <summary>null = not seat-priced; set = monthly price per seat in minor units, seats = users (FR-018-3). Checkout bills it as Stripe quantity (T-033).</summary>
    [JsonPropertyName("perSeatMonthly")]
    public long? PerSeatMonthly { get; set; }

    /// <summary>Parses the row's PriceJson; throws <see cref="InvalidOperationException"/> naming the plan code when it yields no value.</summary>
    /// <param name="plan">The plan whose <see cref="Plan.PriceJson"/> is parsed.</param>
    /// <returns>The deserialized price data.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the operation is not valid in the current state.
    /// </exception>
    public static PlanPrice Read(Plan plan)
    {
        var price = JsonSerializer.Deserialize<PlanPrice>(plan.PriceJson);
        return price ?? throw new InvalidOperationException($"Plan '{plan.Code}' has no price data.");
    }

    /// <summary>Selects the price tier for a billing interval.</summary>
    /// <param name="interval">The billing interval chosen by the user.</param>
    /// <returns>The monthly or annual <see cref="PlanPriceTier"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an argument is outside the allowed range.</exception>
    public PlanPriceTier For(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly => Monthly,
        BillingInterval.Annual => Annual,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
    };
}
