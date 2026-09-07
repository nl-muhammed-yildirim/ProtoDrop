using System.Globalization;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using ILogger = Serilog.ILogger;

namespace wa.api.Telemetry;

/// <summary>
/// Typed telemetry helper (TA-10.2, T-008 part 3). Feature code calls the typed event methods;
/// each emits one structured Serilog line whose <b>message template is the exact TA-10.2 event
/// name</b> and whose properties are restricted to that event's closed TA-10.2 key list —
/// no free-form names can drift from the contract.
/// </summary>
/// <remarks>
/// <para>
/// Locally the line lands wherever the app's existing Serilog config already sends (console/file).
/// When the Application Insights sink is attached (T-008 part 4), its event converter uses
/// the line's message template text as the AI <c>Event</c> name and forwards the line's
/// structured properties as string properties — so the TA-10.2 names stay code. No new packages needed.
/// </para>
/// <para>
/// <c>correlationId</c> (TA-10.1) is carried automatically: <c>CorrelationMiddleware</c> pushes it
/// onto the Serilog log context, so no per-call-site correlation here.
/// </para>
/// <para>
/// Levels per the TA-10.5 mapping: informational business events
/// → <see cref="LogEventLevel.Information"/>; expected business failures (<c>upload_failed</c>, <c>zip_failed</c>,
/// <c>email_failed</c>, <c>login_failed</c>) → <see cref="LogEventLevel.Warning"/>.
/// </para>
/// </remarks>
public sealed class WaTelemetryReporter
{
    private const string MetricValueKey = "value";

    private readonly ILogger _logger;

    public WaTelemetryReporter(ILogger logger)
        => _logger = logger;

    /// <summary><c>upload_started</c> — an upload has started (T-005 retry path or T-007 single-stream).</summary>
    public void UploadStarted()
        => Write(TelemetryEvents.UploadStarted, LogEventLevel.Information);

    /// <summary><c>upload_completed</c> — an upload completed.</summary>
    public void UploadCompleted(long? bytes = null, long? durationMs = null, int? retries = null)
        => Write(TelemetryEvents.UploadCompleted, LogEventLevel.Information,
            (TelemetryEvents.PropertyBytes, bytes?.ToString(CultureInfo.InvariantCulture)),
            (TelemetryEvents.PropertyDurationMs, durationMs?.ToString(CultureInfo.InvariantCulture)),
            (TelemetryEvents.PropertyRetries, retries?.ToString(CultureInfo.InvariantCulture)));

    /// <summary><c>upload_failed</c> — an upload failed.</summary>
    public void UploadFailed(long? bytes = null, long? durationMs = null, int? retries = null)
        => Write(TelemetryEvents.UploadFailed, LogEventLevel.Warning,
            (TelemetryEvents.PropertyBytes, bytes?.ToString(CultureInfo.InvariantCulture)),
            (TelemetryEvents.PropertyDurationMs, durationMs?.ToString(CultureInfo.InvariantCulture)),
            (TelemetryEvents.PropertyRetries, retries?.ToString(CultureInfo.InvariantCulture)));

    /// <summary><c>transfer_page_viewed</c> — a transfer page was rendered.</summary>
    public void TransferPageViewed()
        => Write(TelemetryEvents.TransferPageViewed, LogEventLevel.Information);

    /// <summary><c>transfer_not_found</c> — a transfer page could not be found (404 path).</summary>
    public void TransferNotFound()
        => Write(TelemetryEvents.TransferNotFound, LogEventLevel.Information);

    /// <summary><c>password_correct</c> — a transfer password check succeeded.</summary>
    public void PasswordCorrect()
        => Write(TelemetryEvents.PasswordCorrect, LogEventLevel.Information);

    /// <summary><c>password_wrong</c> — a transfer password check failed.</summary>
    public void PasswordWrong()
        => Write(TelemetryEvents.PasswordWrong, LogEventLevel.Information);

    /// <summary><c>download_started</c> — a download has started.</summary>
    public void DownloadStarted()
        => Write(TelemetryEvents.DownloadStarted, LogEventLevel.Information);

    /// <summary><c>download_completed</c> — a download completed.</summary>
    public void DownloadCompleted()
        => Write(TelemetryEvents.DownloadCompleted, LogEventLevel.Information);

    /// <summary><c>zip_generated</c> — a Zip was generated for multi-file transfer.</summary>
    public void ZipGenerated()
        => Write(TelemetryEvents.ZipGenerated, LogEventLevel.Information);

    /// <summary><c>zip_failed</c> — Zip generation failed.</summary>
    public void ZipFailed()
        => Write(TelemetryEvents.ZipFailed, LogEventLevel.Warning);

    /// <summary><c>email_sent</c> — an email was sent.</summary>
    public void EmailSent(string toEmail)
        => Write(TelemetryEvents.EmailSent, LogEventLevel.Information,
            (TelemetryEvents.PropertyToEmail, toEmail));

    /// <summary><c>email_failed</c> — an email failed to send.</summary>
    public void EmailFailed(string? toEmail = null)
        => Write(TelemetryEvents.EmailFailed, LogEventLevel.Warning,
            (TelemetryEvents.PropertyToEmail, toEmail));

    /// <summary><c>user_created</c> — a user account was created.</summary>
    public void UserCreated(string userEmail)
        => Write(TelemetryEvents.UserCreated, LogEventLevel.Information,
            (TelemetryEvents.PropertyUserEmail, userEmail));

    /// <summary><c>login_success</c> — a login succeeded.</summary>
    public void LoginSuccess(string userEmail)
        => Write(TelemetryEvents.LoginSuccess, LogEventLevel.Information,
            (TelemetryEvents.PropertyUserEmail, userEmail));

    /// <summary><c>login_failed</c> — a login failed.</summary>
    public void LoginFailed(string? userEmail = null)
        => Write(TelemetryEvents.LoginFailed, LogEventLevel.Warning,
            (TelemetryEvents.PropertyUserEmail, userEmail));

    /// <summary><c>account_deleted</c> — a user account was deleted.</summary>
    public void AccountDeleted(string userEmail)
        => Write(TelemetryEvents.AccountDeleted, LogEventLevel.Information,
            (TelemetryEvents.PropertyUserEmail, userEmail));

    /// <summary><c>plan_changed</c> — a plan was changed.</summary>
    public void PlanChanged(string? planFrom = null, string? planTo = null)
        => Write(TelemetryEvents.PlanChanged, LogEventLevel.Information,
            (TelemetryEvents.PropertyPlanFrom, planFrom),
            (TelemetryEvents.PropertyPlanTo, planTo));

    /// <summary><c>translation_missing</c> — a translation key was missing.</summary>
    public void TranslationMissing(string missingKey)
        => Write(TelemetryEvents.TranslationMissing, LogEventLevel.Information,
            (TelemetryEvents.PropertyMissingKey, missingKey));

    /// <summary><c>admin_action</c> — an admin performed an action.</summary>
    public void AdminAction(string? action = null)
        => Write(TelemetryEvents.AdminAction, LogEventLevel.Information,
            (TelemetryEvents.PropertyAction, action));

    /// <summary><c>dlq_count</c> — a dead-letter-queue observation.</summary>
    public void DlqCount(int? count = null)
        => Write(TelemetryEvents.DlqCount, LogEventLevel.Information,
            (TelemetryEvents.PropertyCount, count?.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Low-level TA-10.2 event emit: <paramref name="eventName"/> must be a TA-10.2 event name and
    /// every property key must belong to that event's closed TA-10.2 key list — otherwise
    /// <see cref="ArgumentException"/> (programmer error, so drift fails fast in dev/tests).
    /// </summary>
    public void EmitEvent(string eventName, params (string Key, string? Value)[] properties)
        => Write(eventName, LogEventLevel.Information, properties);

    /// <summary>
    /// Records one measurement for a TA-10.2 metric: the emitted line carries the exact metric
    /// name as its message template and the numeric <c>value</c> property.
    /// </summary>
    public void TrackMeasurement(string metricName, double value)
    {
        ArgumentNullException.ThrowIfNull(metricName);
        if (!TelemetryEvents.Metrics.Contains(metricName))
            throw new ArgumentException($"Unknown TA-10.2 metric name '{metricName}'.", nameof(metricName));

        var props = new List<LogEventProperty>(1);
        props.Add(new LogEventProperty(
            MetricValueKey,
            new ScalarValue(value.ToString(CultureInfo.InvariantCulture))));

        _logger.Write(NewLogEvent(LogEventLevel.Information, metricName, props));
    }

    /// <summary>
    /// Emits one log event whose message template text is <paramref name="eventName"/> and whose
    /// structured properties the AI sink forwards as event properties; null values are skipped
    /// so call-sites can pass partial data.
    /// </summary>
    private void Write(string eventName, LogEventLevel level, params (string Key, string? Value)[] properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (!TelemetryEvents.ContainsEvent(eventName))
            throw new ArgumentException($"Unknown TA-10.2 event name '{eventName}'.", nameof(eventName));

        var props = new List<LogEventProperty>(properties.Length);
        foreach (var (key, value) in properties)
        {
            if (value is null)
                continue;
            props.Add(new LogEventProperty(key, new ScalarValue(value)));
        }

        _logger.Write(NewLogEvent(level, eventName, props));
    }

    /// <summary>
    /// Builds the Serilog event: template text is the exact TA-10.2 name (a single literal token, so
    /// local consoles render the name as the message) and the structured properties are attached.
    /// </summary>
    private static LogEvent NewLogEvent(LogEventLevel level, string name, List<LogEventProperty> properties)
        => new(
            DateTimeOffset.Now,
            level,
            null,
            new MessageTemplate(name, new[] { new TextToken(name) }),
            properties);
}
