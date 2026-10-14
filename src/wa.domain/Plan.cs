namespace wa.domain;

/// <summary>A billing plan (free/pro/business). TA-3.2: dbo.Plan.</summary>
public class Plan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    /// <summary>LimitsRecord JSON (TA-3.4).</summary>
    public string LimitsJson { get; set; } = null!;
    /// <summary>Bool flags: ads, scheduling, analytics, branding.</summary>
    public string FeaturesJson { get; set; } = null!;
    public int SortOrder { get; set; }
}
