using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-3.4 Limits Registry — one POCO holding ALL of Open-Decisions-and-
/// Constants.md Part 2 (Feature Plan Appendix A) for a single plan.
/// Values are always read from <c>Plan.LimitsJson</c> + <c>FeatureFlag</c>
/// rows (TA-3.4); no literal limit values in code (TA-0.2 rule 2).
/// </summary>
public class LimitsRecord
{
    /// <summary><c>MAX_TRANSFER_SIZE</c>, bytes.</summary>
    [JsonPropertyName("maxTransferSize")]
    public long MaxTransferSize { get; set; }

    /// <summary><c>MAX_SINGLE_FILE</c>, bytes (= <c>MAX_TRANSFER_SIZE</c> per Part 2).</summary>
    [JsonPropertyName("maxSingleFile")]
    public long MaxSingleFile { get; set; }

    /// <summary><c>MAX_ZIP_SIZE</c>, bytes.</summary>
    [JsonPropertyName("maxZipSize")]
    public long MaxZipSize { get; set; }

    /// <summary><c>RETENTION_DAYS</c> before expiry job deletes a transfer (TA-6.2).</summary>
    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; set; }

    /// <summary><c>GRACE_DAYS</c> after retention before the link is deleted.</summary>
    [JsonPropertyName("graceDays")]
    public int GraceDays { get; set; }

    /// <summary><c>MAX_DOWNLOADS</c>; -1 = unlimited (Part 2).</summary>
    [JsonPropertyName("maxDownloads")]
    public int MaxDownloads { get; set; }

    /// <summary><c>MAX_EMAILS</c> per batch transfer.</summary>
    [JsonPropertyName("maxEmails")]
    public int MaxEmails { get; set; }

    /// <summary><c>STORAGE_QUOTA</c>, bytes.</summary>
    [JsonPropertyName("storageQuota")]
    public long StorageQuota { get; set; }

    /// <summary><c>ACTIVE_TRANSFERS_MAX</c>; -1 = unlimited (Part 2).</summary>
    [JsonPropertyName("activeTransfersMax")]
    public int ActiveTransfersMax { get; set; }

    /// <summary><c>SCHEDULING</c> gate (Part 2; also a <c>limits.&lt;plan&gt;.*</c> flag key per TA-13.2).</summary>
    [JsonPropertyName("scheduling")]
    public bool Scheduling { get; set; }

    /// <summary><c>BRANDING</c> gate.</summary>
    [JsonPropertyName("branding")]
    public bool Branding { get; set; }

    /// <summary><c>ANALYTICS</c> gate.</summary>
    [JsonPropertyName("analytics")]
    public bool Analytics { get; set; }

    /// <summary><c>ADS</c> gate (D-14: off at launch).</summary>
    [JsonPropertyName("ads")]
    public bool Ads { get; set; }

    /// <summary><c>SSO_SCIM</c> gate (F-ENT-001).</summary>
    [JsonPropertyName("ssoScim")]
    public bool SsoScim { get; set; }
}

/// <summary>
/// TA-3.4 — the API resolves limits per request via this port and never
/// hardcodes a limit value (TA-0.2 rule 2). <see cref="Resolve"/> returns
/// the plan's <see cref="LimitsRecord"/>: <c>Plan.LimitsJson</c> as base,
/// with any matching <c>limits.&lt;plan&gt;.*</c> <c>FeatureFlag</c> value
/// overriding the ONE corresponding field.
/// </summary>
public interface ILimitsProvider
{
    /// <param name="planCode">plan code (e.g. <c>free</c>, <c>pro</c>, <c>business</c> — case-insensitive).</param>
    /// <exception cref="KeyNotFoundException">plan not present in the Plans cache.</exception>
    LimitsRecord Resolve(string planCode);

    /// <summary>Async equivalent of <see cref="Resolve"/> for per-request use in
    /// async API handlers; same semantics, cancellation support.</summary>
    Task<LimitsRecord> ResolveAsync(string planCode, CancellationToken cancellationToken = default);
}
