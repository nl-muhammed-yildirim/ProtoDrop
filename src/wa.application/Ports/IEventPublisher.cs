using wa.domain;

namespace wa.application.Ports;

/// <summary>
/// MediatR-free domain event port (TA-0.2 rule 9): use cases call this, never route events through MediatR's publishing API.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Persists the envelope as an outbox row (same transaction as business data, where one exists) and publishes it to the broker with MessageId = EventId. On success IsPublished is true; on failure the row stays unpublished so re-publish picks it up — no silent drop (EC-054-1).
    /// </summary>
    /// <param name="envelope">TA-5.2 envelope with a TA-5.3 event type; its EventId becomes the broker MessageId.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The stored outbox record, whose publish flag tells whether the broker accepted the message.</returns>
    Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
