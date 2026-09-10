namespace wa.domain;

/// <summary>
/// TA-5.2 / T-006 outbox row. One row per published <see cref="EventEnvelope"/>; keyed
/// by <see cref="EventId"/> (= Service Bus <c>MessageId</c>, the dedup key). The row is
/// written in the caller's transaction (TA-4.1.7) and <see cref="SentAtUtc"/> stays NULL
/// until the publish succeeds, so a crash between the two leaves a retryable row.
///
/// The table is a known gap in the TA-3.2 DDL — the schema row is added per this class
/// (see Open-Decisions-and-Constants.md Part 3 entry for T-006/TA-5.2).
/// </summary>
public class EventOutbox
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique event id. Equals <see cref="EventEnvelope.EventId"/> and the Service Bus
    /// <c>MessageId</c> (TA-5.1 dedup key).
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// One of the closed TA-5.3 event types (e.g. <c>transfer.created</c>).
    /// Equals <see cref="EventEnvelope.EventType"/>.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Serialized TA-5.3 payload (JSON of <see cref="EventEnvelope.Payload"/>).</summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Service Bus partition key (TA-5.3: transferId / address / userId / actor).
    /// Equals <see cref="EventEnvelope.PartitionKey"/>.
    /// </summary>
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>Request correlation id (TA-4.1.3); may be null for ambient emitters.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// UTC timestamp of a successful publish. NULL while unpublished / awaiting retry.
    /// </summary>
    public DateTime? SentAtUtc { get; set; }
}