using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-5.3 closed event-type list — no ad-hoc types at M0. Serialized names are the wire contract (TA-5.2).
/// 12 members at M0 — the 12th (MagicLinkRequested) added for T-008-04 (D-6: EC-008-3 + TA-9.1 single-use token).
/// The 13th (BillingEmailRequested) added for US-019-04 (FR-019-7: billing emails flow through f-email).
/// </summary>
[JsonConverter(typeof(EventTypeConverter))]
public enum EventType
{
    /// <summary>Transfer created.</summary>
    [JsonPropertyName("transfer.created")]
    TransferCreated,

    /// <summary>Transfer expired.</summary>
    [JsonPropertyName("transfer.expired")]
    TransferExpired,

    /// <summary>Transfer deleted.</summary>
    [JsonPropertyName("transfer.deleted")]
    TransferDeleted,

    /// <summary>Email sent.</summary>
    [JsonPropertyName("email.sent")]
    EmailSent,

    /// <summary>Email failed.</summary>
    [JsonPropertyName("email.failed")]
    EmailFailed,

    /// <summary>Download completed.</summary>
    [JsonPropertyName("download.completed")]
    DownloadCompleted,

    /// <summary>ZIP generated.</summary>
    [JsonPropertyName("zip.generated")]
    ZipGenerated,

    /// <summary>User created.</summary>
    [JsonPropertyName("user.created")]
    UserCreated,

    /// <summary>Account deleted.</summary>
    [JsonPropertyName("account.deleted")]
    AccountDeleted,

    /// <summary>Plan changed.</summary>
    [JsonPropertyName("plan.changed")]
    PlanChanged,

    /// <summary>Admin action.</summary>
    [JsonPropertyName("admin.action")]
    AdminAction,

    /// <summary>
    /// T-008-04 (TA-9.1): magic-link request — f-email worker sends the sign-in link. 12th member at M0 (D-6).
    /// </summary>
    [JsonPropertyName("auth.magic_link_requested")]
    MagicLinkRequested,

    /// <summary>
    /// US-019-04 (FR-019-7, D-3): billing email request — f-email worker sends billing_* emails. 13th member.
    /// </summary>
    [JsonPropertyName("billing.email_requested")]
    BillingEmailRequested
}
