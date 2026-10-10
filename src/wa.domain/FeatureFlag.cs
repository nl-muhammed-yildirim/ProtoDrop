namespace wa.domain;

/// <summary>A feature flag / limits override. TA-3.2: dbo.FeatureFlag.</summary>
public class FeatureFlag
{
    /// <summary>Key (VARCHAR(100)) — primary key, e.g. <c>feature.ads</c>, <c>limits.pro.maxFiles</c>.</summary>
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
