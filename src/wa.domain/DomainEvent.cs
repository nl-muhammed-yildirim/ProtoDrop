namespace wa.domain;

/// <summary>An outbox row holding a published domain event (T-006b). TA-3.2: dbo.DomainEvents — new migration per golden rule 4.</summary>
public class DomainEvent
{
    /// <summary>The envelope's EventId — Service Bus MessageId; broker-level dedup identity (TA-5.1).</summary>
    public Guid EventId { get; set; }

    /// <summary>Ta-5.3 wire name, e.g. "transfer.created".</summary>
    public string EventType { get; set; } = null!;

    /// <summary>The exact TA-5.2 serialized envelope (single source of truth for re-publish).</summary>
    public string EnvelopeJson { get; set; } = null!;

    public Guid CorrelationId { get; set; }

    public string? PartitionKey { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>false until a successful publish to the broker; re-publish picks up unpublished rows (EC-054-1).</summary>
    public bool IsPublished { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
