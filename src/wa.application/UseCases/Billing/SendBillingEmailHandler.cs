using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-04 (D-7, FR-019-7): f-email handler for billing.email_requested (kind billing_payment_failed, steps 1..3).
/// Same send / email.sent / email.failed / rethrow shape as SendForgotPasswordEmailHandler; SB redelivers up to MaxDeliveryCount, then DLQ.
/// Billing mail is transactional — no suppression check. Logs never carry the address (TA-9.4).
/// </summary>
public sealed partial class SendBillingEmailHandler : IEmailEventHandler
{
    private static readonly string[] Subjects =
    [
        "Your ProtoDrop payment didn't go through",
        "Reminder: your ProtoDrop payment still needs attention",
        "Last reminder: update your card to keep ProtoDrop Pro"
    ];

    private readonly IEmailSender _emailSender;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _config;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ILogger<SendBillingEmailHandler> _logger;
    private readonly (string Html, string Txt) _templates;
    private readonly (string Html, string Txt) _graceTemplates;

    private const string GraceEndedSubject = "You're back on ProtoDrop Free";
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="SendBillingEmailHandler"/> class.</summary>
    /// <param name="emailSender">Port that delivers the rendered email.</param>
    /// <param name="eventPublisher">Publishes the <c>email.sent</c> and <c>email.failed</c> events.</param>
    /// <param name="config">Configuration source for the public site URL and sender address.</param>
    /// <param name="telemetry">Reporter for the email and dead-letter telemetry events.</param>
    /// <param name="logger">Logger for diagnostics (never logs the address).</param>
    /// <param name="templateRoot">Directory that holds the per-kind template folders.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public SendBillingEmailHandler(
        IEmailSender emailSender,
        IEventPublisher eventPublisher,
        IConfiguration config,
        WaTelemetryReporter telemetry,
        ILogger<SendBillingEmailHandler> logger,
        string templateRoot,
        TimeProvider timeProvider)
    {
        _emailSender = emailSender;
        _eventPublisher = eventPublisher;
        _config = config;
        _telemetry = telemetry;
        _logger = logger;

        _templates = LoadTemplates(templateRoot, "billing-payment-failed");
        _graceTemplates = LoadTemplates(templateRoot, "billing-grace-ended"); // US-019-05 (D-5)
        _timeProvider = timeProvider;
    }

    private static (string Html, string Txt) LoadTemplates(string templateRoot, string folder)
    {
        var basePath = Path.Combine(templateRoot, folder);
        var htmlPath = Path.Combine(basePath, "en.html");
        var txtPath = Path.Combine(basePath, "en.txt");
        if (File.Exists(htmlPath) && File.Exists(txtPath))
            return (File.ReadAllText(htmlPath), File.ReadAllText(txtPath));

        throw new InvalidOperationException(
            $"Billing email templates not found. Expected: {htmlPath} and {txtPath}. " +
            "Verify the workers csproj Content Include for Emails\\templates\\**\\*.");
    }

    /// <inheritdoc />
    public async Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(envelope.Payload);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;

        var email = root.TryGetProperty("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String
            ? emailEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(email))
        {
            LogMissingEmail(_logger);
            return;
        }

        var kind = root.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String
            ? kindEl.GetString() : null;
        var step = 1;
        string subject;
        (string Html, string Txt) templates;
        if (kind == BillingEmailSchedule.Kind)
        {
            if (!root.TryGetProperty("step", out var stepEl) || stepEl.ValueKind != JsonValueKind.Number
                || !stepEl.TryGetInt32(out step) || step < 1 || step > BillingEmailSchedule.StepCount)
            {
                LogMissingStep(_logger);
                return;
            }
            subject = Subjects[step - 1];
            templates = _templates;
        }
        else if (kind == BillingGrace.GraceEndedKind)
        {
            subject = GraceEndedSubject;
            templates = _graceTemplates;
        }
        else
        {
            LogUnknownBillingKind(_logger, kind);
            return;
        }

        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/account";
        var htmlBody = Render(templates.Html, step, ctaUrl);
        var textBody = Render(templates.Txt, step, ctaUrl);

        try
        {
            var fromAddress = _config["Wa:Email:FromAddress"] ?? "no-reply@protodrop.com"; // D-04
            var message = new EmailMessage(email, subject, htmlBody, textBody, fromAddress, null);
            await _emailSender.SendAsync(message, ct);

            await _eventPublisher.PublishAsync(new EventEnvelope
            {
                EventId = Guid.NewGuid(),
                Type = EventType.EmailSent,
                Version = 1,
                OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                CorrelationId = envelope.CorrelationId,
                PartitionKey = email,
                Payload = new { to = email, attempt, kind, step }
            }, ct);

            _telemetry.LogEvent(TelemetryEvents.EmailSent);
        }
        catch (Exception ex)
        {
            LogSendFailed(_logger, ex, step, attempt);

            if (attempt == maxDeliveryCount)
            {
                // Final failure (TA-5.1): broker dead-letters after this throw — emit the observables first.
                await _eventPublisher.PublishAsync(new EventEnvelope
                {
                    EventId = Guid.NewGuid(),
                    Type = EventType.EmailFailed,
                    Version = 1,
                    OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    CorrelationId = envelope.CorrelationId,
                    PartitionKey = email,
                    Payload = new { to = email, attempt = maxDeliveryCount, reason = ex.GetType().Name, kind, step }
                }, ct);
                _telemetry.LogEvent(TelemetryEvents.DlqCount);
                _telemetry.EmailFailures1h();
            }

            throw;
        }
    }

    private static string Render(string template, int step, string ctaUrl) =>
        template.Replace("{{Step}}", step.ToString()).Replace("{{CtaUrl}}", ctaUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-email: missing or invalid email in billing payload")]
    private static partial void LogMissingEmail(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-email: missing or invalid step in billing payload")]
    private static partial void LogMissingStep(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-email: unknown billing email kind {Kind}")]
    private static partial void LogUnknownBillingKind(ILogger logger, string? kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-email: failed to send billing email step {Step} attempt {Attempt}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, int step, int attempt);
}
