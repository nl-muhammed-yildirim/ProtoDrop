using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Events;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-006-01: f-email consumer handler — sends notification emails for transfer.created events.
/// Two-layer dedup: EventDeduplicator (eventId, 15-min window) + NotifiedAtUtc null check per recipient row.
/// </summary>
public sealed class SendNotificationEmailsHandler
{
    private readonly IEmailStore _store;
    private readonly IEmailSender _emailSender;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IEventPublisher _eventPublisher;
    private readonly EventDeduplicator _deduplicator;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IConfiguration _config;
    private readonly ILogger<SendNotificationEmailsHandler> _logger;

    public SendNotificationEmailsHandler(
        IEmailStore store,
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer,
        IEventPublisher eventPublisher,
        EventDeduplicator deduplicator,
        WaTelemetryReporter telemetry,
        IConfiguration config,
        ILogger<SendNotificationEmailsHandler> logger)
    {
        _store = store;
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _eventPublisher = eventPublisher;
        _deduplicator = deduplicator;
        _telemetry = telemetry;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct)
    {
        // 1. Deserialize payload → transferId + emails[].
        var payloadJson = JsonSerializer.Serialize(envelope.Payload);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;

        if (!root.TryGetProperty("transferId", out var transferIdEl) ||
            !transferIdEl.TryGetGuid(out var transferId))
        {
            _logger.LogWarning("f-email: missing or invalid transferId in payload");
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
            _logger.LogWarning("f-email: transfer {TransferId} not found", transferId);
            return;
        }

        // 4. Dedup: if already seen → return (fast-path skip).
        if (_deduplicator.AlreadySeen(envelope.EventId))
        {
            _logger.LogInformation("f-email: event {EventId} already processed, skipping", envelope.EventId);
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

        // 7. Render template once (same data for all recipients in one transfer).
        var expiryDays = (int)Math.Ceiling((context.ExpiresAtUtc - DateTime.UtcNow).TotalDays);
        var publicOrigin = _config["Wa:Url:Public"] ?? "http://localhost:5173";
        var ctaUrl = $"{publicOrigin}/t/{context.LinkId}";
        var templateData = new TemplateData(
            context.SenderName,
            context.Files,
            ctaUrl,
            expiryDays);
        var (subject, htmlBody, textBody) = _templateRenderer.Render(templateData);

        // 8. Send emails.
        var fromAddress = _config["Wa:Email:FromAddress"] ?? "no-reply@protodrop.com";
        var notifiedIds = new List<Guid>();

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
                    OccurredAtUtc = DateTime.UtcNow,
                    CorrelationId = envelope.CorrelationId,
                    PartitionKey = context.LinkId,
                    Payload = new
                    {
                        transferId = context.TransferId,
                        to = recipient.Address,
                        attempt = attempt
                    }
                };
                await _eventPublisher.PublishAsync(emailSentEnvelope, ct);

                // Telemetry.
                _telemetry.LogEvent(TelemetryEvents.EmailSent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "f-email: failed to send email to {Address} attempt {Attempt}", recipient.Address, attempt);

                if (attempt == maxDeliveryCount)
                {
                    // Final failure (TA-5.1): broker dead-letters after this throw — emit the observables first.
                    var reason = ex.GetType().Name; // no Azure SDK dep in wa.application (dependency rule TA-0.2); full detail is in the log line
                    var emailFailedEnvelope = new EventEnvelope
                    {
                        EventId = Guid.NewGuid(),
                        Type = EventType.EmailFailed,
                        Version = 1,
                        OccurredAtUtc = DateTime.UtcNow,
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
            await _store.MarkNotifiedAsync(notifiedIds, DateTime.UtcNow, ct);
        }
    }
}
