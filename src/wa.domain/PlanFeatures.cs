using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>Shape contract for <see cref="Plan.FeaturesJson"/> (F-BIL-001): five boolean feature flags. <see cref="PlanDefinition"/> uses the JsonPropertyName set as the exact key contract.</summary>
public class PlanFeatures
{
    /// <summary>True when the plan's downloads show advertising.</summary>
    [JsonPropertyName("ads")]
    public bool Ads { get; set; }

    /// <summary>True when the plan may schedule a transfer for a later send time.</summary>
    [JsonPropertyName("scheduling")]
    public bool Scheduling { get; set; }

    /// <summary>True when the plan includes the download analytics view.</summary>
    [JsonPropertyName("analytics")]
    public bool Analytics { get; set; }

    /// <summary>True when the plan may attach an organization name and logo to its transfers.</summary>
    [JsonPropertyName("branding")]
    public bool Branding { get; set; }

    /// <summary>True when the plan includes SSO and SCIM provisioning.</summary>
    [JsonPropertyName("sso_scim")]
    public bool SsoScim { get; set; }
}
