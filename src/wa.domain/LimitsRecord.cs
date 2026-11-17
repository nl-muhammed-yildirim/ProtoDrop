using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>Plan limit constants — every constant from Feature Plan Appendix A (Open-Decisions Part 2). TA-3.4: deserialized 1:1 from the seeded <see cref="Plan.LimitsJson"/> (T-052-04 seeds); camelCase key names are fixed here so the contract does not depend on serializer options.</summary>
public class LimitsRecord
{
    /// <summary>MAX_TRANSFER_SIZE, bytes.</summary>
    [JsonPropertyName("maxTransferSize")]
    public long MaxTransferSize { get; set; }

    /// <summary>MAX_SINGLE_FILE, bytes (same as transfer size for all plans).</summary>
    [JsonPropertyName("maxSingleFile")]
    public long MaxSingleFile { get; set; }

    /// <summary>MAX_ZIP_SIZE, bytes; 9223372036854775807 = unlimited (Part 2).</summary>
    [JsonPropertyName("maxZipSize")]
    public long MaxZipSize { get; set; }

    /// <summary>RETENTION_DAYS.</summary>
    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; set; }

    /// <summary>GRACE_DAYS after expiry before physical deletion.</summary>
    [JsonPropertyName("graceDays")]
    public int GraceDays { get; set; }

    /// <summary>MAX_DOWNLOADS; -1 = unlimited (Part 2).</summary>
    [JsonPropertyName("maxDownloads")]
    public int MaxDownloads { get; set; }

    /// <summary>MAX_EMAILS recipients per transfer.</summary>
    [JsonPropertyName("maxEmails")]
    public int MaxEmails { get; set; }

    /// <summary>STORAGE_QUOTA, bytes.</summary>
    [JsonPropertyName("storageQuota")]
    public long StorageQuota { get; set; }

    /// <summary>ACTIVE_TRANSFERS_MAX; -1 = unlimited (Part 2).</summary>
    [JsonPropertyName("activeTransfersMax")]
    public int ActiveTransfersMax { get; set; }

    /// <summary>True when the plan's MAX_DOWNLOADS is unlimited (-1 sentinel, Part 2). Callers use this instead of raw -1 checks (T-007-01).</summary>
    public bool IsMaxDownloadsInfinite() => MaxDownloads < 0;

    /// <summary>True when the plan's ACTIVE_TRANSFERS_MAX is unlimited (-1 sentinel, Part 2). Callers use this instead of raw -1 checks (T-007-01).</summary>
    public bool IsActiveTransfersMaxInfinite() => ActiveTransfersMax < 0;

    /// <summary>True when the plan's MAX_ZIP_SIZE is unlimited (long.MaxValue sentinel, Part 2). Callers use this instead of raw long.MaxValue checks (T-007-01).</summary>
    public bool IsMaxZipSizeInfinite() => MaxZipSize == long.MaxValue;
}