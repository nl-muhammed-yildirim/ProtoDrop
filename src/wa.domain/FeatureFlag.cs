namespace wa.domain;

/// <summary>A feature flag / limits override. TA-3.2: dbo.FeatureFlag.</summary>
public class FeatureFlag
{
    /// <summary>Key (VARCHAR(100)) — primary key, e.g. <c>feature.ads</c>, <c>limits.pro.maxFiles</c>.</summary>
    public string Key { get; set; } = null!;
    /// <summary>Raw flag value as text; interpreted by the flag's type (boolean, number or string).</summary>
    public string Value { get; set; } = null!;
    /// <summary>Admin-facing explanation of what the flag controls; may be null.</summary>
    public string? Description { get; set; }
    /// <summary>UTC instant the value was last changed.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
