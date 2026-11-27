namespace wa.workers;

/// <summary>
/// CHORE-local-email (TA-6.7): the one place that turns a serialized TA-5.2 event envelope into a call to the matching email handler.
/// Both the Service Bus trigger (f-email) and the local HTTP hand-off trigger (f-email-handoff) call it, so the two paths cannot drift.
/// </summary>
public interface IEmailEnvelopeDispatcher
{
    /// <summary>Deserializes the envelope and dispatches it to the handler for its event type; event types without an email handler are ignored.</summary>
    /// <param name="envelopeJson">The serialized TA-5.2 envelope.</param>
    /// <param name="deliveryCount">Which delivery attempt this is (1-based); the last attempt lets the handler report a failure.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>A task that completes when the handler has finished.</returns>
    /// <exception cref="Exception">Whatever the handler throws; it is logged (event id and attempt only) and rethrown so the caller can retry.</exception>
    Task DispatchAsync(string envelopeJson, int deliveryCount, CancellationToken ct);
}
