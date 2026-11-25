namespace wa.domain.Telemetry;

/// <summary>
/// The closed TA-10.2 telemetry contract — "names are code" (TA-10.2): exactly these 26 event names + 7 metric names, no others in M0.
/// Adding a member later is an architecture change (EC-056-4: it requires adding the name to TA-10.2 first). No renames — wire values are what App Insights queries by.
/// </summary>
public static class TelemetryEvents
{
    // ---- Events (TA-10.2 line 816) — only upload_completed carries contract-named properties: bytes, durationMs, retries ----

    /// <summary>The upload started constant; value <c>upload_started</c>.</summary>
    public const string UploadStarted = "upload_started";
    /// <summary>The upload completed constant; value <c>upload_completed</c>.</summary>
    public const string UploadCompleted = "upload_completed";
    /// <summary>The upload failed constant; value <c>upload_failed</c>.</summary>
    public const string UploadFailed = "upload_failed";
    /// <summary>The transfer page viewed constant; value <c>transfer_page_viewed</c>.</summary>
    public const string TransferPageViewed = "transfer_page_viewed";
    /// <summary>The transfer not found constant; value <c>transfer_not_found</c>.</summary>
    public const string TransferNotFound = "transfer_not_found";
    /// <summary>The password correct constant; value <c>password_correct</c>.</summary>
    public const string PasswordCorrect = "password_correct";
    /// <summary>The password wrong constant; value <c>password_wrong</c>.</summary>
    public const string PasswordWrong = "password_wrong";
    /// <summary>The download started constant; value <c>download_started</c>.</summary>
    public const string DownloadStarted = "download_started";
    /// <summary>The download completed constant; value <c>download_completed</c>.</summary>
    public const string DownloadCompleted = "download_completed";
    /// <summary>The ZIP generated constant; value <c>zip_generated</c>.</summary>
    public const string ZipGenerated = "zip_generated";
    /// <summary>The ZIP failed constant; value <c>zip_failed</c>.</summary>
    public const string ZipFailed = "zip_failed";
    /// <summary>The email sent constant; value <c>email_sent</c>.</summary>
    public const string EmailSent = "email_sent";
    /// <summary>The email failed constant; value <c>email_failed</c>.</summary>
    public const string EmailFailed = "email_failed";
    /// <summary>The user created constant; value <c>user_created</c>.</summary>
    public const string UserCreated = "user_created";
    /// <summary>The login success constant; value <c>login_success</c>.</summary>
    public const string LoginSuccess = "login_success";
    /// <summary>The login failed constant; value <c>login_failed</c>.</summary>
    public const string LoginFailed = "login_failed";
    /// <summary>The account deleted constant; value <c>account_deleted</c>.</summary>
    public const string AccountDeleted = "account_deleted";
    /// <summary>The plan changed constant; value <c>plan_changed</c>.</summary>
    public const string PlanChanged = "plan_changed";
    /// <summary>The translation missing constant; value <c>translation_missing</c>.</summary>
    public const string TranslationMissing = "translation_missing";
    /// <summary>The admin action constant; value <c>admin_action</c>.</summary>
    public const string AdminAction = "admin_action";
    /// <summary>The dlq count constant; value <c>dlq_count</c>.</summary>
    public const string DlqCount = "dlq_count";
    /// <summary>The email template fallback constant; value <c>email.template_fallback</c>.</summary>
    public const string EmailTemplateFallback = "email.template_fallback";
    /// <summary>The template render error constant; value <c>template_render_error</c>.</summary>
    public const string TemplateRenderError = "template_render_error";
    /// <summary>US-021-04 (EC-021-2): property-free.</summary>
    public const string BrandingLogoMissing = "branding_logo_missing";
    /// <summary>US-024-01 (F-PRF-004): client-only, props slotId.</summary>
    public const string AdImpression = "ad_impression";
    /// <summary>US-024-01 (F-PRF-004): client-only, props slotId + reason.</summary>
    public const string AdError = "ad_error";

    // ---- Metrics (TA-10.2 line 818) — same pipeline, no separate metrics SDK ----

    /// <summary>5-minute window.</summary>
    public const string UploadSuccessRate = "upload.success_rate";
    /// <summary>The link to download seconds constant; value <c>link_to_download_seconds</c>.</summary>
    public const string LinkToDownloadSeconds = "link_to_download_seconds";
    /// <summary>The expiry job lag seconds constant; value <c>expiry_job_lag_seconds</c>.</summary>
    public const string ExpiryJobLagSeconds = "expiry_job_lag_seconds";
    /// <summary>The email failures1h constant; value <c>email_failures_1h</c>.</summary>
    public const string EmailFailures1h = "email_failures_1h";
    /// <summary>The active transfers constant; value <c>active_transfers</c>.</summary>
    public const string ActiveTransfers = "active_transfers";
    /// <summary>The storage bytes active constant; value <c>storage_bytes_active</c>.</summary>
    public const string StorageBytesActive = "storage_bytes_active";
    /// <summary>{status} is the wire suffix, not a member.</summary>
    public const string ApiRequestsByStatus = "api.requests{status}";

    private static readonly string[] Events =
    [
        UploadStarted, UploadCompleted, UploadFailed, TransferPageViewed, TransferNotFound, PasswordCorrect, PasswordWrong,
        DownloadStarted, DownloadCompleted, ZipGenerated, ZipFailed, EmailSent, EmailFailed, UserCreated, LoginSuccess,
        LoginFailed, AccountDeleted, PlanChanged, TranslationMissing, AdminAction, DlqCount,
        EmailTemplateFallback, TemplateRenderError, BrandingLogoMissing,
        AdImpression, AdError
    ];

    private static readonly string[] Metrics =
    [
        UploadSuccessRate, LinkToDownloadSeconds, ExpiryJobLagSeconds, EmailFailures1h, ActiveTransfers, StorageBytesActive, ApiRequestsByStatus
    ];

    /// <summary>Every closed-set name — events then metrics (33 total). Used by the T-056-04 exit-check test to assert a 1:1 map against the TA-10.2 literals.</summary>
    /// <returns>The matching text value items.</returns>
    public static IReadOnlyList<string> AllNames() => Events.Concat(Metrics).ToArray();

    /// <summary>true iff the name is a closed-set event (not a metric, not an ad-hoc string).</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when event; otherwise <see langword="false"/>.</returns>
    public static bool IsEvent(string name) => Events.Contains(name);
}
