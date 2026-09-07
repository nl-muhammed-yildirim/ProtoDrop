using System.Collections.Generic;

namespace wa.api.Telemetry;

/// <summary>
/// TA-10.2 event-name contract: the closed list of 21 custom event
/// names (F-TRF-012 FR-012-1; "names are code" per TA-10.2) plus the
/// property KEY names each event carries. Feature code emits telemetry
/// through <see cref="WaTelemetryReporter"/> so the closed list cannot
/// drift from free-form strings.
///
/// TA-10.5 PII rule: no PII at the default log level. PII-only
/// properties (e.g. <c>Email</c>) are declared with <c>PiiFlag=true</c>
/// so CI (EC-012-3) can grep call sites for raw email patterns; the
/// value is stored SHA-256(HMAC) hashed per F-TRF-012 and the reporter
/// marks it <c>Pii=true</c> so App Insights (future) indexes it as
/// PII instead of a normal searchable field.
/// </summary>
public static class TelemetryEvents
{
    //
    // The 21 TA-10.2 event names. These strings ARE the queryable
    // App Insights query surface (TA-10.3 / TA-10.4 dashboards and
    // alerts key off these exact values) - never rename without a
    // migration.
    //

    public const string UploadStarted = "upload_started";
    public const string UploadCompleted = "upload_completed";
    public const string UploadFailed = "upload_failed";
    public const string TransferPageViewed = "transfer_page_viewed";
    public const string TransferNotFound = "transfer_not_found";
    public const string PasswordCorrect = "password_correct";
    public const string PasswordWrong = "password_wrong";
    public const string DownloadStarted = "download_started";
    public const string DownloadCompleted = "download_completed";
    public const string ZipGenerated = "zip_generated";
    public const string ZipFailed = "zip_failed";
    public const string EmailSent = "email_sent";
    public const string EmailFailed = "email_failed";
    public const string UserCreated = "user_created";
    public const string LoginSuccess = "login_success";
    public const string LoginFailed = "login_failed";
    public const string AccountDeleted = "account_deleted";
    public const string PlanChanged = "plan_changed";
    public const string TranslationMissing = "translation_missing";
    public const string AdminAction = "admin_action";
    public const string DlqCount = "dlq_count";

    //
    // The 7 TA-10.2 custom metric names (dashboards TA-10.4 key off
    // these exact values; windows are dashboard-side, not code-side).
    //

    public const string MetricUploadSuccessRate = "upload.success_rate";
    public const string MetricLinkToDownloadSeconds = "link_to_download_seconds";
    public const string MetricExpiryJobLagSeconds = "expiry_job_lag_seconds";
    public const string MetricEmailFailures1h = "email_failures_1h";
    public const string MetricActiveTransfers = "active_transfers";
    public const string MetricStorageBytesActive = "storage_bytes_active";
    public const string MetricApiRequests = "api.requests";

    //
    // Property keys per event, per TA-10.2. Keys are the exact
    // App Insights property bag names the query surface (TA-10.3 /
    // TA-10.4) reads.
    //

    public const string PropertyBytes = "bytes";
    public const string PropertyCount = "count";
    public const string PropertyDurationMs = "durationMs";
    public const string PropertyRetries = "retries";
    public const string PropertyPlanFrom = "planFrom";
    public const string PropertyPlanTo = "planTo";
    public const string PropertyMissingKey = "missingKey";
    public const string PropertyAction = "action";
    public const string PropertyStatus = "status";
    public const string PropertyToEmail = "toEmail";
    public const string PropertyUserEmail = "userEmail";

    /// <summary>
    /// Per-event closed property key sets. A call to
    /// <see cref="WaTelemetryReporter"/> builds its bag from these
    /// sets only - no ad-hoc keys outside this list.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PropertyKeys =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [UploadCompleted] = new[] { PropertyBytes, PropertyDurationMs, PropertyRetries },
            [UploadFailed] = new[] { PropertyBytes, PropertyDurationMs, PropertyRetries },
            [DownloadStarted] = new[] { PropertyDurationMs, PropertyRetries },
            [DownloadCompleted] = new[] { PropertyDurationMs, PropertyRetries },
            [ZipGenerated] = new[] { PropertyDurationMs, PropertyRetries },
            [ZipFailed] = new[] { PropertyDurationMs, PropertyRetries },
            [EmailFailed] = new[] { PropertyRetries },
            [LoginFailed] = new[] { PropertyStatus },
            [PlanChanged] = new[] { PropertyPlanFrom, PropertyPlanTo },
            [TranslationMissing] = new[] { PropertyMissingKey },
            [AdminAction] = new[] { PropertyAction },
            [DlqCount] = new[] { PropertyStatus },
            // upload_started, transfer_page_viewed, transfer_not_found,
            // password_*, email_sent, user_created, login_success,
            // account_deleted carry no TA-10.2-mandated keys in this step.
        };

    /// <summary>
    /// The 7 TA-10.2 metric names as a closed set, for fast containment checks.
    /// </summary>
    public static readonly IReadOnlySet<string> Metrics = new HashSet<string>
    {
        MetricUploadSuccessRate,
        MetricLinkToDownloadSeconds,
        MetricExpiryJobLagSeconds,
        MetricEmailFailures1h,
        MetricActiveTransfers,
        MetricStorageBytesActive,
        MetricApiRequests,
    };

    /// <summary>True if <paramref name="eventName"/> is one of the 21 TA-10.2 event names.</summary>
    public static bool ContainsEvent(string eventName)
        => eventName is
            UploadStarted or UploadCompleted or UploadFailed or
            TransferPageViewed or TransferNotFound or
            PasswordCorrect or PasswordWrong or
            DownloadStarted or DownloadCompleted or
            ZipGenerated or ZipFailed or
            EmailSent or EmailFailed or
            UserCreated or LoginSuccess or LoginFailed or AccountDeleted or
            PlanChanged or TranslationMissing or AdminAction or
            DlqCount;
}
