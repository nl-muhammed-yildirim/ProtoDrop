namespace wa.application.Limits;

/// <summary>
/// US-044-02 (FR-044-3, FR-044-7): the closed set of phase-gate flag keys. These are identifiers, not limit values.
/// Every consumer references these constants — the key literals live nowhere else in src/ (guarded by a domain-unit test).
/// </summary>
public static class FeatureGates
{
    // TA-13.2 gate prefix; keys are built from it so each full key spelling lives only in the seed (FeaturesJsonConsumerGuardTests, FeatureGateKeyLiteralGuardTests).
    private const string Prefix = "feature.";

    public const string Search = Prefix + "search";
    public const string DataExport = Prefix + "dataExport";
    public const string EmailSettings = Prefix + "emailSettings";
    public const string Ads = Prefix + "ads";
    public const string Scheduling = Prefix + "scheduling";
    public const string Analytics = Prefix + "analytics";

    /// <summary>All six gates, in the story's order.</summary>
    public static readonly IReadOnlyList<string> All = [Search, DataExport, EmailSettings, Ads, Scheduling, Analytics];

    /// <summary>FR-044-3 / EC-044-6: a missing or unreadable gate row means the feature is off.</summary>
    public const bool DefaultWhenAbsent = false;
}
