using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-5.2 event envelope — exactly these seven fields, no more, no less (AC-054-2).
/// Serialized byte-exact with plain <c>JsonSerializer</c> calls — no per-call options (AC-054-2): camelCase keys pinned by [JsonPropertyName], and EventType's bound converter emits its TA-5.3 wire name.
/// </summary>
public class EventEnvelope
{
    /// <summary>The domain event id — Service Bus MessageId (dedup identity, TA-5.1).</summary>
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    /// <summary>Closed list from TA-5.3.</summary>
    [JsonPropertyName("eventType")]
    public EventType Type { get; set; }

    /// <summary>Semantic version of the event schema — 1 for everything at M0.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>When the fact happened (UTC), not when it was published.</summary>
    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Copies the command's correlation id through the event stream.</summary>
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; set; }

    /// <summary>Routing key for Service Bus partitioning (TA-5.1: topic core).</summary>
    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; set; } = null!;

    /// <summary>The MediatR-free domain event POCO, serialized as its JSON representation.</summary>
    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}
