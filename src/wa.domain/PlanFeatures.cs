using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>Shape contract for <see cref="Plan.FeaturesJson"/> (F-BIL-001): five boolean feature flags. <see cref="PlanDefinition"/> uses the JsonPropertyName set as the exact key contract.</summary>
public class PlanFeatures
{
    /// <summary>Gets or sets a value indicating whether ads applies to the plan features.</summary>
    [JsonPropertyName("ads")]
    public bool Ads { get; set; }

    /// <summary>Gets or sets a value indicating whether scheduling applies to the plan features.</summary>
    [JsonPropertyName("scheduling")]
    public bool Scheduling { get; set; }

    /// <summary>Gets or sets a value indicating whether analytics applies to the plan features.</summary>
    [JsonPropertyName("analytics")]
    public bool Analytics { get; set; }

    /// <summary>Gets or sets a value indicating whether branding applies to the plan features.</summary>
    [JsonPropertyName("branding")]
    public bool Branding { get; set; }

    /// <summary>Gets or sets a value indicating whether SSO scim applies to the plan features.</summary>
    [JsonPropertyName("sso_scim")]
    public bool SsoScim { get; set; }
}
