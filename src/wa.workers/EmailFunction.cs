using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.workers;

/// <summary>
/// T-006-01: f-email Azure Function — Service Bus trigger for email notifications.
/// Concurrency 10, 5-min timeout (TA-6.2).
/// T-008-04 (D-6): dispatches on EventType — TransferCreated → SendNotificationEmailsHandler; MagicLinkRequested → SendMagicLinkEmailHandler.
/// </summary>
public class EmailFunction
{
    /// <summary>MaxDeliveryCount for the core/email subscription (TA-5.1) — broker-side; passed to the handler as a plain int so wa.application stays SB-free.</summary>
    private const int MaxDeliveryCount = 5;

    private readonly SendNotificationEmailsHandler _notificationHandler;
    private readonly SendMagicLinkEmailHandler _magicLinkHandler;
    private readonly ILogger<EmailFunction> _logger;

    public EmailFunction(
        SendNotificationEmailsHandler notificationHandler,
        SendMagicLinkEmailHandler magicLinkHandler,
        ILogger<EmailFunction> logger)
    {
        _notificationHandler = notificationHandler;
        _magicLinkHandler = magicLinkHandler;
        _logger = logger;
    }

    [Function("f-email")]
    public async Task Run(
        FunctionContext context,
        [ServiceBusTrigger("core", "email", Connection = "Wa:ServiceBus:ConnectionString")] ServiceBusReceivedMessage message)
    {
        // Deserialize EventEnvelope from JSON.
        var body = message.Body.ToString(); // BinaryData.ToString() decodes UTF-8 — no extra using needed
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(body);
        if (envelope is null)
            return;

        // T-008-04 (D-6): route on event type — only these two types reach this subscription.
        var handler = envelope.Type switch
        {
            EventType.TransferCreated => _notificationHandler,
            EventType.MagicLinkRequested => _magicLinkHandler,
            _ => null
        };

        if (handler is null)
            return;

        try
        {
            await handler.HandleAsync(envelope, message.DeliveryCount, MaxDeliveryCount, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-email failed for event {EventId} attempt {Attempt}", envelope.EventId, message.DeliveryCount);
            throw; // SB retries on unhandled exception
        }
    }
}
