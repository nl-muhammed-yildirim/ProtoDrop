namespace wa.application.Limits;

/// <summary>
/// US-044-02 (FR-044-3, FR-044-7): the closed set of phase-gate flag keys. These are identifiers, not limit values.
/// Every consumer references these constants — the key literals live nowhere else in src/ (guarded by a domain-unit test).
/// </summary>
public static class FeatureGates
{
    /// <summary>The search constant; value <c>feature.search</c>.</summary>
    public const string Search = "feature.search";
    /// <summary>The data export constant; value <c>feature.dataExport</c>.</summary>
    public const string DataExport = "feature.dataExport";
    /// <summary>The email settings constant; value <c>feature.emailSettings</c>.</summary>
    public const string EmailSettings = "feature.emailSettings";
    /// <summary>The ads constant; value <c>feature.ads</c>.</summary>
    public const string Ads = "feature.ads";
    /// <summary>The scheduling constant; value <c>feature.scheduling</c>.</summary>
    public const string Scheduling = "feature.scheduling";
    /// <summary>The analytics constant; value <c>feature.analytics</c>.</summary>
    public const string Analytics = "feature.analytics";

    /// <summary>All six gates, in the story's order.</summary>
    public static readonly IReadOnlyList<string> All = [Search, DataExport, EmailSettings, Ads, Scheduling, Analytics];

    /// <summary>FR-044-3 / EC-044-6: a missing or unreadable gate row means the feature is off.</summary>
    public const bool DefaultWhenAbsent = false;
}
