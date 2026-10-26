namespace wa.domain.Telemetry;

/// <summary>
/// The closed TA-10.2 telemetry contract — "names are code" (TA-10.2): exactly these 23 event names + 7 metric names, no others in M0.
/// Adding a member later is an architecture change (EC-056-4: it requires adding the name to TA-10.2 first). No renames — wire values are what App Insights queries by.
/// </summary>
public static class TelemetryEvents
{
    // ---- Events (TA-10.2 line 816) — only upload_completed carries contract-named properties: bytes, durationMs, retries ----

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
    public const string EmailTemplateFallback = "email.template_fallback";
    public const string TemplateRenderError = "template_render_error";

    // ---- Metrics (TA-10.2 line 818) — same pipeline, no separate metrics SDK ----

    public const string UploadSuccessRate = "upload.success_rate"; // 5-minute window
    public const string LinkToDownloadSeconds = "link_to_download_seconds";
    public const string ExpiryJobLagSeconds = "expiry_job_lag_seconds";
    public const string EmailFailures1h = "email_failures_1h";
    public const string ActiveTransfers = "active_transfers";
    public const string StorageBytesActive = "storage_bytes_active";
    public const string ApiRequestsByStatus = "api.requests{status}"; // {status} is the wire suffix, not a member

    private static readonly string[] Events =
    [
        UploadStarted, UploadCompleted, UploadFailed, TransferPageViewed, TransferNotFound, PasswordCorrect, PasswordWrong,
        DownloadStarted, DownloadCompleted, ZipGenerated, ZipFailed, EmailSent, EmailFailed, UserCreated, LoginSuccess,
        LoginFailed, AccountDeleted, PlanChanged, TranslationMissing, AdminAction, DlqCount,
        EmailTemplateFallback, TemplateRenderError
    ];

    private static readonly string[] Metrics =
    [
        UploadSuccessRate, LinkToDownloadSeconds, ExpiryJobLagSeconds, EmailFailures1h, ActiveTransfers, StorageBytesActive, ApiRequestsByStatus
    ];

    /// <summary>Every closed-set name — events then metrics (28 total). Used by the T-056-04 exit-check test to assert a 1:1 map against the TA-10.2 literals.</summary>
    public static IReadOnlyList<string> AllNames() => Events.Concat(Metrics).ToArray();

    /// <summary>true iff the name is a closed-set event (not a metric, not an ad-hoc string).</summary>
    public static bool IsEvent(string name) => Events.Contains(name);
}
