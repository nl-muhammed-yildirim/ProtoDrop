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

    /// <summary>Gets or sets the correlation ID of the domain event.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Gets or sets the partition key of the domain event.</summary>
    public string? PartitionKey { get; set; }

    /// <summary>Gets or sets the occurred at UTC of the domain event.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>false until a successful publish to the broker; re-publish picks up unpublished rows (EC-054-1).</summary>
    public bool IsPublished { get; set; }

    /// <summary>Gets or sets the created at UTC of the domain event.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
