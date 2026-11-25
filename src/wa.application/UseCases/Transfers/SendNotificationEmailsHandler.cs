using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Events;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-006-01: f-email consumer handler — sends notification emails for transfer.created events.
/// Two-layer dedup: EventDeduplicator (eventId, 15-min window) + NotifiedAtUtc null check per recipient row.
/// </summary>
/// <param name="store">Store that reads the transfer, recipients and suppressions and records notifications.</param>
/// <param name="emailSender">Port that delivers the rendered emails.</param>
/// <param name="templateRenderer">Renders the email in the recipient's language.</param>
/// <param name="eventPublisher">Publishes the <c>email.sent</c> and <c>email.failed</c> events.</param>
/// <param name="deduplicator">Skips events that were already handled in the dedup window.</param>
/// <param name="telemetry">Reporter for the email telemetry events.</param>
/// <param name="config">Configuration source for the public site URL, sender address and unsubscribe signing key.</param>
/// <param name="logger">Logger for diagnostics (never logs addresses).</param>
/// <param name="branding">Store that reads the sender's organization name.</param>
/// <param name="features">Provider of the sender plan's feature toggles.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed partial class SendNotificationEmailsHandler(
    IEmailStore store,
    IEmailSender emailSender,
    ITemplateRenderer templateRenderer,
    IEventPublisher eventPublisher,
    EventDeduplicator deduplicator,
    WaTelemetryReporter telemetry,
    IConfiguration config,
    ILogger<SendNotificationEmailsHandler> logger,
    ISenderBrandingStore branding,
    IFeaturesProvider features,
    TimeProvider timeProvider) : wa.application.UseCases.Auth.IEmailEventHandler
{
    private readonly IEmailStore _store = store;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly ITemplateRenderer _templateRenderer = templateRenderer;
    private readonly IEventPublisher _eventPublisher = eventPublisher;
    private readonly EventDeduplicator _deduplicator = deduplicator;
    private readonly WaTelemetryReporter _telemetry = telemetry;
    private readonly IConfiguration _config = config;
    private readonly ILogger<SendNotificationEmailsHandler> _logger = logger;
    private readonly ISenderBrandingStore _branding = branding;
    private readonly IFeaturesProvider _features = features;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)
    {
        // 1. Deserialize payload → transferId + emails[].
        var payloadJson = JsonSerializer.Serialize(envelope.Payload);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;

        if (!root.TryGetProperty("transferId", out var transferIdEl) ||
            !transferIdEl.TryGetGuid(out var transferId))
        {
            LogMissingTransferId(_logger);
            return;
        }

        // 2. If emails is empty/null → return (FR-006-6 short-circuit).
        if (!root.TryGetProperty("emails", out var emailsEl) ||
            emailsEl.ValueKind != JsonValueKind.Array ||
            emailsEl.GetArrayLength() == 0)
        {
            return;
        }

        // 3. Load context from store; if null → log + return.
        var context = await _store.GetTransferWithFilesAsync(transferId, ct);
        if (context is null)
        {
            LogTransferNotFound(_logger, transferId);
            return;
        }

        // 3b. US-022-04 (FR-022-4, D-1): a scheduled transfer may notify only while its DB status is Active. Runs before dedup so a
        // skipped delivery is not recorded as seen; returns (no throw) so the message completes. The timer re-emits transfer.created on flip.
        if (!ScheduledSend.MayNotify(context.Status, context.ScheduledSendAtUtc))
        {
            LogTransferNotDue(_logger, transferId);
            return;
        }

        // 4. Dedup: if already seen → return (fast-path skip).
        if (_deduplicator.AlreadySeen(envelope.EventId))
        {
            LogEventAlreadyProcessed(_logger, envelope.EventId);
            return;
        }

        // 5. Get recipients where NotifiedAtUtc IS NULL.
        var recipients = await _store.GetRecipientsAsync(transferId, ct);
        var unNotified = recipients.Where(r => r.NotifiedAtUtc is null).ToList();
        if (unNotified.Count == 0)
            return;

        // 6. Batch-check suppressions for all addresses at once.
        var addressesToCheck = unNotified.Select(r => r.Address).ToList();
        var suppressed = await _store.GetSuppressedAddressesAsync(addressesToCheck, context.SenderEmail, ct);

        // 7. Prepare template data (same for all recipients; rendered per-recipient with their locale — T-006-04).
        var expiryDays = (int)Math.Ceiling((context.ExpiresAtUtc - _timeProvider.GetUtcNow().UtcDateTime).TotalDays);
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/t/{context.LinkId}";
        // F-PRF-001 US-021-02 (D-1): org resolved once at dispatch; the rendered email is the send-time snapshot. Never logged (D-6).
        var orgName = await SenderBranding.ResolveOrgNameAsync(_branding, _features, context.OwnerAppUserId, ct);
        var templateData = new TemplateData(
            SenderBranding.ComposeEmailSenderLine(context.SenderName, orgName),
            context.Files,
            ctaUrl,
            expiryDays);

        // 8. Send emails.
        var fromAddress = _config["Wa:Email:FromAddress"] ?? "no-reply@protodrop.com";
        var notifiedIds = new List<Guid>();
        var jwtSecret = _config["Wa:Jwt:Secret"];

        foreach (var recipient in unNotified)
        {
            // Skip if address == SenderEmail (case-insensitive, FR-006-3).
            if (!string.IsNullOrEmpty(context.SenderEmail) &&
                string.Equals(recipient.Address, context.SenderEmail, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip if suppressed.
            if (suppressed.Contains(recipient.Address))
            {
                continue;
            }

            // T-014-04: Resolve locale using pure resolver (ADR-011 per-recipient attribute).
            var locale = TemplateLocaleResolver.Resolve(recipient.PreferredLanguage);
            var perRecipientData = templateData with
            {
                UnsubscribeUrl = BuildUnsubscribeUrl(publicOrigin, recipient.Address, context.SenderEmail, jwtSecret)
            };
            var (subject, htmlBody, textBody) = _templateRenderer.Render(perRecipientData, locale);

            try
            {
                var emailMessage = new EmailMessage(
                    recipient.Address,
                    subject,
                    htmlBody,
                    textBody,
                    fromAddress,
                    context.SenderEmail);

                await _emailSender.SendAsync(emailMessage, ct);
                notifiedIds.Add(recipient.Id);

                // Emit email.sent event.
                var emailSentEnvelope = new EventEnvelope
                {
                    EventId = Guid.NewGuid(),
                    Type = EventType.EmailSent,
                    Version = 1,
                    OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    CorrelationId = envelope.CorrelationId,
                    PartitionKey = context.LinkId,
                    Payload = new
                    {
                        transferId = context.TransferId,
                        to = recipient.Address,
                        attempt = attempt,
                        locale // T-014-04: TA-10.2 — email_sent carries the locale used
                    }
                };
                await _eventPublisher.PublishAsync(emailSentEnvelope, ct);

                // Telemetry.
                _telemetry.LogEvent(TelemetryEvents.EmailSent);
            }
            catch (Exception ex)
            {
                LogSendFailed(_logger, ex, recipient.Id, attempt);

                if (attempt == maxDeliveryCount)
                {
                    // Final failure (TA-5.1): broker dead-letters after this throw — emit the observables first.
                    var reason = ex.GetType().Name; // no Azure SDK dep in wa.application (dependency rule TA-0.2); full detail is in the log line
                    var emailFailedEnvelope = new EventEnvelope
                    {
                        EventId = Guid.NewGuid(),
                        Type = EventType.EmailFailed,
                        Version = 1,
                        OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                        CorrelationId = envelope.CorrelationId,
                        PartitionKey = context.LinkId,
                        Payload = new
                        {
                            transferId = context.TransferId,
                            to = recipient.Address,
                            attempt = maxDeliveryCount,
                            reason
                        }
                    };
                    await _eventPublisher.PublishAsync(emailFailedEnvelope, ct);
                    _telemetry.LogEvent(TelemetryEvents.DlqCount); // dlq_count > 0 alert (P2, TA-10.3)
                    _telemetry.EmailFailures1h(); // email_failures_1h metric, threshold 50/h (TA-10.3)
                }

                throw; // transient → redeliver; on last attempt the broker dead-letters (MaxDeliveryCount 5 ⇒ 3 visible retries, backoff ≈ 1 min / 10 min / 1 h — FR-006-4)
            }
        }

        // 9. MarkNotifiedAsync for all successfully sent.
        if (notifiedIds.Count > 0)
        {
            await _store.MarkNotifiedAsync(notifiedIds, _timeProvider.GetUtcNow().UtcDateTime, ct);
        }
    }

    /// <summary>T-006-05 (FR-006-2/5): per-recipient unsubscribe link; null when sender or signing secret is unavailable.</summary>
    private static string? BuildUnsubscribeUrl(string publicOrigin, string address, string? senderEmail, string? secret)
    {
        if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(secret))
            return null;

        var token = UnsubscribeToken.Create(address, senderEmail, Encoding.UTF8.GetBytes(secret));
        return $"{publicOrigin}/unsubscribe/{token}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-email: missing or invalid transferId in payload")]
    private static partial void LogMissingTransferId(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "f-email: transfer {TransferId} not found")]
    private static partial void LogTransferNotFound(ILogger logger, Guid transferId);

    [LoggerMessage(Level = LogLevel.Information, Message = "f-email: transfer {TransferId} is not due for notification, skipping")]
    private static partial void LogTransferNotDue(ILogger logger, Guid transferId);

    [LoggerMessage(Level = LogLevel.Information, Message = "f-email: event {EventId} already processed, skipping")]
    private static partial void LogEventAlreadyProcessed(ILogger logger, Guid eventId);

    [LoggerMessage(Level = LogLevel.Error, Message = "f-email: failed to send email to recipient {RecipientId} attempt {Attempt}")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid recipientId, int attempt);
}
