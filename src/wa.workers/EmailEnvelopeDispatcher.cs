using System.Text.Json;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Billing;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Correlation;

namespace wa.workers;

/// <summary>
/// T-006-01: shared email dispatch behind the f-email Service Bus trigger (Concurrency 10, 5-min timeout, TA-6.2) and, since
/// CHORE-local-email, the local f-email-handoff HTTP trigger.
/// T-008-04 (D-6): dispatches on EventType — TransferCreated → SendNotificationEmailsHandler; MagicLinkRequested → SendMagicLinkEmailHandler.
/// T-008-05 (D-3): MagicLinkRequested now forks on the payload's purpose field — purpose=2 routes to the new
/// SendForgotPasswordEmailHandler, everything else to SendMagicLinkEmailHandler. The switch is annotated
/// IEmailEventHandler? (shared interface in wa.application), which also fixes the pre-existing CS8506 build defect.
/// US-019-04 (FR-019-7): BillingEmailRequested → SendBillingEmailHandler (billing_payment_failed, steps 1..3).
/// </summary>
/// <param name="notificationHandler">Handler for <c>transfer.created</c> recipient notifications.</param>
/// <param name="magicLinkHandler">Handler for sign-in link emails.</param>
/// <param name="forgotPasswordHandler">Handler for password-reset emails.</param>
/// <param name="billingHandler">Handler for billing emails.</param>
/// <param name="logger">Logger for the correlation scope and dispatch failures (never logs addresses).</param>
public sealed partial class EmailEnvelopeDispatcher(
    SendNotificationEmailsHandler notificationHandler,
    SendMagicLinkEmailHandler magicLinkHandler,
    SendForgotPasswordEmailHandler forgotPasswordHandler,
    SendBillingEmailHandler billingHandler,
    ILogger<EmailEnvelopeDispatcher> logger) : IEmailEnvelopeDispatcher
{
    /// <summary>MaxDeliveryCount for the core/email subscription (TA-5.1) — broker-side; passed to the handler as a plain int so wa.application stays SB-free.</summary>
    public const int MaxDeliveryCount = 5;

    private readonly SendNotificationEmailsHandler _notificationHandler = notificationHandler;
    private readonly SendMagicLinkEmailHandler _magicLinkHandler = magicLinkHandler;
    private readonly SendForgotPasswordEmailHandler _forgotPasswordHandler = forgotPasswordHandler;
    private readonly SendBillingEmailHandler _billingHandler = billingHandler;
    private readonly ILogger<EmailEnvelopeDispatcher> _logger = logger;

    /// <inheritdoc />
    public async Task DispatchAsync(string envelopeJson, int deliveryCount, CancellationToken ct)
    {
        // Deserialize EventEnvelope from JSON.
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(envelopeJson);
        if (envelope is null)
            return;

        // T-012-01: derive the W3C traceparent from the envelope's CorrelationId — it is a W3C trace-id (32-hex) when the event was published from a request; generate fresh otherwise.
        var traceId = envelope.CorrelationId.ToString("n");
        var tp = (traceId.Length == 32 && !traceId.All(c => c == '0'))
            ? new TraceParent(W3CTraceParser.SupportedVersion, traceId, Guid.NewGuid().ToString("n")[..16], "07")
            : W3CTraceParser.New();
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = tp.CorrelationId, ["TraceParent"] = tp.ToHeader() });

        // T-008-04 (D-6) + T-008-05 (D-3): route on event type — only these types have an email handler.
        // Annotated IEmailEventHandler? so all arms (including null) share one type — fixes the pre-existing CS8506.
        IEmailEventHandler? handler = envelope.Type switch
        {
            EventType.TransferCreated => _notificationHandler,
            EventType.MagicLinkRequested => IsForgotPasswordPayload(envelope) ? _forgotPasswordHandler : _magicLinkHandler,
            EventType.BillingEmailRequested => _billingHandler,
            _ => null
        };

        if (handler is null)
            return;

        try
        {
            await handler.HandleAsync(envelope, deliveryCount, MaxDeliveryCount, ct);
        }
        catch (Exception ex)
        {
            LogEventFailed(_logger, ex, envelope.EventId, deliveryCount);
            throw; // SB retries on unhandled exception
        }
    }

    /// <summary>
    /// T-008-05 (D-3): true when the payload's purpose field is 2 (forgot-password), false otherwise.
    /// Serializing-then-parsing mirrors what SendMagicLinkEmailHandler itself does — same idiom as the codebase.
    /// </summary>
    private static bool IsForgotPasswordPayload(EventEnvelope envelope)
    {
        var json = JsonSerializer.Serialize(envelope.Payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("purpose", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() == 2;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "f-email failed for event {EventId} attempt {Attempt}")]
    private static partial void LogEventFailed(ILogger logger, Exception exception, Guid eventId, int attempt);
}
