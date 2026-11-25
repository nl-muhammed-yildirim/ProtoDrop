namespace wa.domain.Telemetry;

/// <summary>
/// The closed TA-10.2 telemetry contract — "names are code" (TA-10.2): exactly these 26 event names + 7 metric names, no others in M0.
/// Adding a member later is an architecture change (EC-056-4: it requires adding the name to TA-10.2 first). No renames — wire values are what App Insights queries by.
/// </summary>
public static class TelemetryEvents
{
    // ---- Events (TA-10.2 line 816) — only upload_completed carries contract-named properties: bytes, durationMs, retries ----

    /// <summary>Event <c>upload_started</c>: a sender began uploading the first file of a transfer.</summary>
    public const string UploadStarted = "upload_started";
    /// <summary>Event <c>upload_completed</c>: all files of a transfer were uploaded; carries the contract properties bytes, durationMs and retries.</summary>
    public const string UploadCompleted = "upload_completed";
    /// <summary>Event <c>upload_failed</c>: an upload ended in an error and the sender must retry.</summary>
    public const string UploadFailed = "upload_failed";
    /// <summary>Event <c>transfer_page_viewed</c>: a recipient opened a transfer download page.</summary>
    public const string TransferPageViewed = "transfer_page_viewed";
    /// <summary>Event <c>transfer_not_found</c>: a download page was requested for an unknown or deleted link.</summary>
    public const string TransferNotFound = "transfer_not_found";
    /// <summary>Event <c>password_correct</c>: a recipient entered the right password of a protected transfer.</summary>
    public const string PasswordCorrect = "password_correct";
    /// <summary>Event <c>password_wrong</c>: a recipient entered a wrong password of a protected transfer.</summary>
    public const string PasswordWrong = "password_wrong";
    /// <summary>Event <c>download_started</c>: a recipient started downloading a file or the zip.</summary>
    public const string DownloadStarted = "download_started";
    /// <summary>Event <c>download_completed</c>: a download request was served and counted against the transfer's download limit.</summary>
    public const string DownloadCompleted = "download_completed";
    /// <summary>Event <c>zip_generated</c>: the zip of a multi-file transfer was built successfully.</summary>
    public const string ZipGenerated = "zip_generated";
    /// <summary>Event <c>zip_failed</c>: building the zip failed.</summary>
    public const string ZipFailed = "zip_failed";
    /// <summary>Event <c>email_sent</c>: a notification email was accepted by the mail provider.</summary>
    public const string EmailSent = "email_sent";
    /// <summary>Event <c>email_failed</c>: sending a notification email failed.</summary>
    public const string EmailFailed = "email_failed";
    /// <summary>Event <c>user_created</c>: a new account was created.</summary>
    public const string UserCreated = "user_created";
    /// <summary>Event <c>login_success</c>: a user signed in successfully.</summary>
    public const string LoginSuccess = "login_success";
    /// <summary>Event <c>login_failed</c>: a sign-in attempt was rejected.</summary>
    public const string LoginFailed = "login_failed";
    /// <summary>Event <c>account_deleted</c>: a user deleted their account.</summary>
    public const string AccountDeleted = "account_deleted";
    /// <summary>Event <c>plan_changed</c>: a user's plan changed (upgrade, downgrade or admin change).</summary>
    public const string PlanChanged = "plan_changed";
    /// <summary>Event <c>translation_missing</c> (client-emitted): the UI asked for an i18n key that has no translation in the active language.</summary>
    public const string TranslationMissing = "translation_missing";
    /// <summary>Event <c>admin_action</c>: an administrator performed an audited action.</summary>
    public const string AdminAction = "admin_action";
    /// <summary>Event <c>dlq_count</c>: an email message exhausted its delivery attempts and is left to dead-letter (TA-5.1).</summary>
    public const string DlqCount = "dlq_count";
    /// <summary>Event <c>email.template_fallback</c>: an email was rendered in the English fallback because the recipient's language has no template.</summary>
    public const string EmailTemplateFallback = "email.template_fallback";
    /// <summary>Event <c>template_render_error</c>: rendering an email template threw.</summary>
    public const string TemplateRenderError = "template_render_error";
    /// <summary>Event <c>branding_logo_missing</c> (US-021-04, EC-021-2): a branded transfer's logo blob could not be found; carries no properties.</summary>
    public const string BrandingLogoMissing = "branding_logo_missing";
    /// <summary>Event <c>ad_impression</c> (US-024-01, F-PRF-004): an ad slot was shown; emitted by the client only, with the property slotId.</summary>
    public const string AdImpression = "ad_impression";
    /// <summary>Event <c>ad_error</c> (US-024-01, F-PRF-004): an ad slot failed to load; emitted by the client only, with the properties slotId and reason.</summary>
    public const string AdError = "ad_error";

    // ---- Metrics (TA-10.2 line 818) — same pipeline, no separate metrics SDK ----

    /// <summary>Metric <c>upload.success_rate</c>: share of successful uploads, evaluated over a 5-minute window.</summary>
    public const string UploadSuccessRate = "upload.success_rate";
    /// <summary>Metric <c>link_to_download_seconds</c>: seconds between a transfer being sent and its first download.</summary>
    public const string LinkToDownloadSeconds = "link_to_download_seconds";
    /// <summary>Metric <c>expiry_job_lag_seconds</c>: how late the expiry job ran relative to its schedule.</summary>
    public const string ExpiryJobLagSeconds = "expiry_job_lag_seconds";
    /// <summary>Metric <c>email_failures_1h</c>: number of failed emails in the last hour.</summary>
    public const string EmailFailures1h = "email_failures_1h";
    /// <summary>Metric <c>active_transfers</c>: number of transfers currently in an active state.</summary>
    public const string ActiveTransfers = "active_transfers";
    /// <summary>Metric <c>storage_bytes_active</c>: total blob bytes held by active transfers.</summary>
    public const string StorageBytesActive = "storage_bytes_active";
    /// <summary>Metric <c>api.requests{status}</c>: API request count split by HTTP status; <c>{status}</c> is the wire suffix, not a member.</summary>
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
    /// <returns>All event names followed by all metric names, as a new array.</returns>
    public static IReadOnlyList<string> AllNames() => Events.Concat(Metrics).ToArray();

    /// <summary>true iff the name is a closed-set event (not a metric, not an ad-hoc string).</summary>
    /// <param name="name">Telemetry name to test.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is one of the closed event names; otherwise <see langword="false"/>.</returns>
    public static bool IsEvent(string name) => Events.Contains(name);
}
