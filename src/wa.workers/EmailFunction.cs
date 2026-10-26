using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.workers;

/// <summary>
/// T-006-01: f-email Azure Function — Service Bus trigger for email notifications.
/// Concurrency 10, 5-min timeout (TA-6.2).
/// </summary>
public class EmailFunction
{
    /// <summary>MaxDeliveryCount for the core/email subscription (TA-5.1) — broker-side; passed to the handler as a plain int so wa.application stays SB-free.</summary>
    private const int MaxDeliveryCount = 5;

    private readonly SendNotificationEmailsHandler _handler;
    private readonly ILogger<EmailFunction> _logger;

    public EmailFunction(SendNotificationEmailsHandler handler, ILogger<EmailFunction> logger)
    {
        _handler = handler;
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
        if (envelope is null || envelope.Type != EventType.TransferCreated)
            return;

        try
        {
            await _handler.HandleAsync(envelope, message.DeliveryCount, MaxDeliveryCount, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-email failed for event {EventId} attempt {Attempt}", envelope.EventId, message.DeliveryCount);
            throw; // SB retries on unhandled exception
        }
    }
}
