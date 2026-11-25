namespace wa.domain;

/// <summary>A billing plan (free/pro/business). TA-3.2: dbo.Plan.</summary>
public class Plan
{
    /// <summary>Code of the seeded default plan every new account starts on (FR-018-6, TA-3.4: never hardcoded elsewhere). Callers read this instead of a "free" literal.</summary>
    public const string DefaultCode = "free";

    /// <summary>Gets or sets the ID of the plan.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the code of the plan.</summary>
    public string Code { get; set; } = null!;
    /// <summary>Gets or sets the name of the plan.</summary>
    public string Name { get; set; } = null!;
    /// <summary>LimitsRecord JSON (TA-3.4).</summary>
    public string LimitsJson { get; set; } = null!;
    /// <summary>Bool flags: ads, scheduling, analytics, branding, sso_scim — shape defined by <see cref="PlanFeatures"/>.</summary>
    public string FeaturesJson { get; set; } = null!;
    /// <summary>Prices (FR-018-3) — shape defined by <see cref="PlanPrice"/>; read via <see cref="PlanPrice.Read"/>, never parsed elsewhere.</summary>
    public string PriceJson { get; set; } = null!;
    /// <summary>Gets or sets the sort order of the plan.</summary>
    public int SortOrder { get; set; }
}
