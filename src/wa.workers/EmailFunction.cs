using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;

namespace wa.workers;

/// <summary>
/// T-006-01: f-email Azure Function — Service Bus trigger for email notifications (TA-6.2). Hands the message to the shared
/// <see cref="IEmailEnvelopeDispatcher"/> (CHORE-local-email), which also serves the local HTTP hand-off trigger.
/// </summary>
/// <param name="dispatcher">Shared dispatch of an event envelope to its email handler.</param>
public class EmailFunction(IEmailEnvelopeDispatcher dispatcher)
{
    private readonly IEmailEnvelopeDispatcher _dispatcher = dispatcher;

    /// <summary>Service Bus entry point for the <c>email</c> subscription: passes the serialized envelope and the delivery attempt to the dispatcher, so the last attempt can report a failure before the broker dead-letters the message.</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="message">The received Service Bus message whose body is the serialized TA-5.2 envelope.</param>
    /// <returns>A task that completes when the message has been dispatched.</returns>
    [Function("f-email")]
    public async Task Run(
        FunctionContext context,
        [ServiceBusTrigger("core", "email", Connection = "Wa:ServiceBus:ConnectionString")] ServiceBusReceivedMessage message)
    {
        await _dispatcher.DispatchAsync(message.Body.ToString(), message.DeliveryCount, context.CancellationToken); // BinaryData.ToString() decodes UTF-8
    }
}
