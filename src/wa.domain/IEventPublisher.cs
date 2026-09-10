namespace wa.domain;

/// <summary>
/// TA-0.2 rule 9 / TA-5.2 - the event-publishing port. Domain events stay
/// MediatR-free: use cases call <see cref="PublishAsync"/> (outbox row +
/// publish) instead of MediatR <c>IPublisher.Publish(DomainEvent)</c>, so
/// <c>wa.application</c> never depends on MediatR's eventing.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Enqueue the event (outbox row, keyed by <see cref="EventEnvelope.EventId"/>)
    /// and publish it to Service Bus (TA-5.1 topic <c>core</c>).
    /// </summary>
    /// <param name="envelope">
    /// The TA-5.2 envelope; <see cref="EventEnvelope.EventId"/> and
    /// <see cref="EventEnvelope.EventType"/> must be set, and the event type
    /// must be one of the closed TA-5.3 list.
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default);
}