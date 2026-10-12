using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-5.3 closed event-type list — no ad-hoc types at M0. Serialized names are the wire contract (TA-5.2).
/// </summary>
public enum EventType
{
    [JsonPropertyName("transfer.created")]
    TransferCreated,

    [JsonPropertyName("transfer.expired")]
    TransferExpired,

    [JsonPropertyName("transfer.deleted")]
    TransferDeleted,

    [JsonPropertyName("email.sent")]
    EmailSent,

    [JsonPropertyName("email.failed")]
    EmailFailed,

    [JsonPropertyName("download.completed")]
    DownloadCompleted,

    [JsonPropertyName("zip.generated")]
    ZipGenerated,

    [JsonPropertyName("user.created")]
    UserCreated,

    [JsonPropertyName("account.deleted")]
    AccountDeleted,

    [JsonPropertyName("plan.changed")]
    PlanChanged,

    [JsonPropertyName("admin.action")]
    AdminAction
}

/// <summary>
/// TA-5.2 event envelope — exactly these seven fields, no more, no less (AC-054-2).
/// Serialized with camelCase keys pinned by <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/> so the contract does not depend on serializer options.
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
