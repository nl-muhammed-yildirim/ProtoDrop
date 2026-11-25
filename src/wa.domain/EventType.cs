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
    /// <summary>A sender finalized a transfer; the wire name is <c>transfer.created</c>.</summary>
    [JsonPropertyName("transfer.created")]
    TransferCreated,

    /// <summary>A transfer passed its expiry time; the wire name is <c>transfer.expired</c>.</summary>
    [JsonPropertyName("transfer.expired")]
    TransferExpired,

    /// <summary>A transfer was deleted by its owner or by cleanup; the wire name is <c>transfer.deleted</c>.</summary>
    [JsonPropertyName("transfer.deleted")]
    TransferDeleted,

    /// <summary>A notification email was handed to the mail provider; the wire name is <c>email.sent</c>.</summary>
    [JsonPropertyName("email.sent")]
    EmailSent,

    /// <summary>Sending a notification email failed permanently; the wire name is <c>email.failed</c>.</summary>
    [JsonPropertyName("email.failed")]
    EmailFailed,

    /// <summary>A recipient finished downloading a file or the zip; the wire name is <c>download.completed</c>.</summary>
    [JsonPropertyName("download.completed")]
    DownloadCompleted,

    /// <summary>The zip of a multi-file transfer was built; the wire name is <c>zip.generated</c>.</summary>
    [JsonPropertyName("zip.generated")]
    ZipGenerated,

    /// <summary>A new account was created; the wire name is <c>user.created</c>.</summary>
    [JsonPropertyName("user.created")]
    UserCreated,

    /// <summary>An account was deleted (GDPR erasure starts); the wire name is <c>account.deleted</c>.</summary>
    [JsonPropertyName("account.deleted")]
    AccountDeleted,

    /// <summary>A user's plan changed through billing or admin action; the wire name is <c>plan.changed</c>.</summary>
    [JsonPropertyName("plan.changed")]
    PlanChanged,

    /// <summary>An administrator performed an audited action; the wire name is <c>admin.action</c>.</summary>
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
