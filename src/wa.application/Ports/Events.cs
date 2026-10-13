using wa.domain;

namespace wa.application.Ports;

/// <summary>
/// An outbox row for a published domain event, returned by the publisher after the envelope is persisted and (on success) delivered to the broker.
/// </summary>
public class DomainEventRecord
{
    /// <summary>The envelope's EventId — Service Bus MessageId; broker-level dedup identity (TA-5.1).</summary>
    public Guid EventId { get; init; }

    /// <summary>true after a successful publish to the broker; stays false on failure so re-publish picks it up (EC-054-1).</summary>
    public bool IsPublished { get; set; }
}

/// <summary>
/// MediatR-free domain event port (TA-0.2 rule 9): use cases call this, never route events through MediatR's publishing API.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Persists the envelope as an outbox row (same transaction as business data, where one exists) and publishes it to the broker with MessageId = EventId. On success IsPublished is true; on failure the row stays unpublished so re-publish picks it up — no silent drop (EC-054-1).
    /// </summary>
    Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
