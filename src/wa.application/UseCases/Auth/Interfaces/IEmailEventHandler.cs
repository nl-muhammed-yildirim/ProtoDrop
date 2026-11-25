using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-05 (D-3): shared contract for f-email handlers so EmailFunction's dispatch switch has a common type (fixes CS8506).
/// Lives in wa.application so all handlers stay Service-Bus-free (the workers project owns the SB trigger).
/// </summary>
public interface IEmailEventHandler
{
    /// <summary>Processes one email-related event delivered by Service Bus and sends or records the resulting email.</summary>
    /// <param name="envelope">The deserialized TA-5.2 envelope of the event.</param>
    /// <param name="attempt">Current delivery attempt, starting at 1.</param>
    /// <param name="maxDeliveryCount">Delivery attempts after which the broker dead-letters the message.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task HandleAsync(EventEnvelope envelope, int attempt, int maxDeliveryCount, CancellationToken ct);
}
