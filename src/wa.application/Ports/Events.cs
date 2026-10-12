using wa.domain;

namespace wa.application.Ports;

/// <summary>
/// An outbox row for a published domain event. Written via the EF Core change tracker in the same transaction as business data (T-006b).
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
    /// Writes the envelope as a <c>DomainEvents</c> row via the EF Core change tracker (same transaction as business data), then publishes it to Service Bus topic <c>core</c> with MessageId = EventId. On success marks IsPublished; on failure leaves false so re-publish picks it up — no silent drop (EC-054-1).
    /// </summary>
    Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken);
}
