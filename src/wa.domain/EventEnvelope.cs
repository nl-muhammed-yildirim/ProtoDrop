using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-5.2 - the Service Bus message envelope. EVERY background event (the
/// closed TA-5.3 list only) is wrapped in this shape before it leaves
/// <c>wa-api</c>. The wire JSON field names are exactly <c>eventId</c>,
/// <c>eventType</c>, <c>version</c>, <c>occurredAt</c>, <c>correlationId</c>,
/// <c>partitionKey</c>, <c>payload</c>.
/// </summary>
public sealed record EventEnvelope
{
    /// <summary>
    /// <see cref="System.Guid"/>-shaped unique event id. This is the Service
    /// Bus <c>MessageId</c> and the dedup key: consumers dedup on it (TA-5.1,
    /// TA-5.2), and the outbox row is keyed by it for replay safety.
    /// </summary>
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = default!;

    /// <summary>
    /// One of the closed TA-5.3 event types (e.g. <c>transfer.created</c>,
    /// <c>email.sent</c>). No other types in the MVP.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = default!;

    /// <summary>Envelope schema version (TA-5.2; currently <c>1</c>).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>When the event occurred (UTC, ISO 8601 per TA-5.2).</summary>
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Request correlation id for end-to-end tracing (TA-4.1.3 / US-012-01).
    /// May be absent for ambient (non-request) emitters.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Service Bus partition key that preserves ordering (TA-5.2). Per TA-5.3:
    /// <c>transferId</c>, <c>address</c>, <c>userId</c>, or <c>actor</c>.
    /// </summary>
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = default!;

    /// <summary>
    /// The TA-5.3 payload for <see cref="EventType"/> (a POCO of that event
    /// type's shape, or <c>null</c> for events with no payload).
    /// </summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; init; }
}
