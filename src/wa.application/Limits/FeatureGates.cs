namespace wa.application.Limits;

/// <summary>
/// US-044-02 (FR-044-3, FR-044-7): the closed set of phase-gate flag keys. These are identifiers, not limit values.
/// Every consumer references these constants — the key literals live nowhere else in src/ (guarded by a domain-unit test).
/// </summary>
public static class FeatureGates
{
    /// <summary>Phase gate for transfer search (FR-045); the flag key is <c>feature.search</c>.</summary>
    public const string Search = "feature.search";
    /// <summary>Phase gate for the GDPR data export (FR-047); the flag key is <c>feature.dataExport</c>.</summary>
    public const string DataExport = "feature.dataExport";
    /// <summary>Phase gate for owner email-notification settings (FR-046); the flag key is <c>feature.emailSettings</c>.</summary>
    public const string EmailSettings = "feature.emailSettings";
    /// <summary>Phase gate for advertising slots (F-PRF-004); the flag key is <c>feature.ads</c>.</summary>
    public const string Ads = "feature.ads";
    /// <summary>Phase gate for scheduled sends (F-PRF-002); the flag key is <c>feature.scheduling</c>.</summary>
    public const string Scheduling = "feature.scheduling";
    /// <summary>Phase gate for download analytics (F-PRF-003); the flag key is <c>feature.analytics</c>.</summary>
    public const string Analytics = "feature.analytics";

    /// <summary>All six gates, in the story's order.</summary>
    public static readonly IReadOnlyList<string> All = [Search, DataExport, EmailSettings, Ads, Scheduling, Analytics];

    /// <summary>FR-044-3 / EC-044-6: a missing or unreadable gate row means the feature is off.</summary>
    public const bool DefaultWhenAbsent = false;
}
