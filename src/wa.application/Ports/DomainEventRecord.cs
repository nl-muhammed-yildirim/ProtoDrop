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
