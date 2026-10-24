using System.Text.Json;
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
        [ServiceBusTrigger("core", "email", Connection = "Wa:ServiceBus:ConnectionString")] string messageBody)
    {
        // Deserialize EventEnvelope from JSON.
        var envelope = JsonSerializer.Deserialize<EventEnvelope>(messageBody);
        if (envelope is null || envelope.Type != EventType.TransferCreated)
            return;

        try
        {
            await _handler.HandleAsync(envelope, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-email failed for event {EventId}", envelope.EventId);
            throw; // SB retries on unhandled exception
        }
    }
}
