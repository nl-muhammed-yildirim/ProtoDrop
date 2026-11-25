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
    [JsonPropertyName("transfer.created")]
    TransferCreated,

    [JsonPropertyName("transfer.expired")]
    TransferExpired,

    [JsonPropertyName("transfer.deleted")]
    TransferDeleted,

    [JsonPropertyName("email.sent")]
    EmailSent,

    [JsonPropertyName("email.failed")]
    EmailFailed,

    [JsonPropertyName("download.completed")]
    DownloadCompleted,

    [JsonPropertyName("zip.generated")]
    ZipGenerated,

    [JsonPropertyName("user.created")]
    UserCreated,

    [JsonPropertyName("account.deleted")]
    AccountDeleted,

    [JsonPropertyName("plan.changed")]
    PlanChanged,

    [JsonPropertyName("admin.action")]
    AdminAction,

    // T-008-04 (TA-9.1): magic-link request — f-email worker sends the sign-in link. 12th member at M0 (D-6).
    [JsonPropertyName("auth.magic_link_requested")]
    MagicLinkRequested,

    // US-019-04 (FR-019-7, D-3): billing email request — f-email worker sends billing_* emails. 13th member.
    [JsonPropertyName("billing.email_requested")]
    BillingEmailRequested
}
