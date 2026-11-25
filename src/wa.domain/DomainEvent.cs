namespace wa.domain;

/// <summary>An outbox row holding a published domain event (T-006b). TA-3.2: dbo.DomainEvents — new migration per golden rule 4.</summary>
public class DomainEvent
{
    /// <summary>The envelope's EventId — Service Bus MessageId; broker-level dedup identity (TA-5.1).</summary>
    public Guid EventId { get; set; }

    /// <summary>TA-5.3 wire name, e.g. "transfer.created".</summary>
    public string EventType { get; set; } = null!;

    /// <summary>The exact TA-5.2 serialized envelope (single source of truth for re-publish).</summary>
    public string EnvelopeJson { get; set; } = null!;

    /// <summary>Correlation id of the request that raised the event, so consumers can continue the same trace (TA-10.1).</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Optional Service Bus partition/session key that keeps related events ordered (for example the transfer id).</summary>
    public string? PartitionKey { get; set; }

    /// <summary>UTC instant the domain fact happened (the envelope time, not the publish time).</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>false until a successful publish to the broker; re-publish picks up unpublished rows (EC-054-1).</summary>
    public bool IsPublished { get; set; }

    /// <summary>UTC instant the outbox row was written.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
