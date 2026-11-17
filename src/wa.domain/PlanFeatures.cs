using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>Shape contract for <see cref="Plan.FeaturesJson"/> (F-BIL-001): five boolean feature flags. <see cref="PlanDefinition"/> uses the JsonPropertyName set as the exact key contract.</summary>
public class PlanFeatures
{
    [JsonPropertyName("ads")]
    public bool Ads { get; set; }

    [JsonPropertyName("scheduling")]
    public bool Scheduling { get; set; }

    [JsonPropertyName("analytics")]
    public bool Analytics { get; set; }

    [JsonPropertyName("branding")]
    public bool Branding { get; set; }

    [JsonPropertyName("sso_scim")]
    public bool SsoScim { get; set; }
}
