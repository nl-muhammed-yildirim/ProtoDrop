using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// TA-5.3 closed event-type list — no ad-hoc types at M0. Serialized names are the wire contract (TA-5.2).
/// 12 members at M0 — the 12th (MagicLinkRequested) added for T-008-04 (D-6: EC-008-3 + TA-9.1 single-use token).
/// </summary>
[JsonConverter(typeof(EventTypeConverter))]
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
    AdminAction,

    // T-008-04 (TA-9.1): magic-link request — f-email worker sends the sign-in link. 12th member at M0 (D-6).
    [JsonPropertyName("auth.magic_link_requested")]
    MagicLinkRequested
}

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

/// <summary>
/// Serializes <see cref="EventType"/> as its TA-5.3 wire name (e.g. "transfer.created") — the dotted form no naming policy can produce from member names, so plain <c>JsonSerializer</c> calls stay option-free. Bound via [JsonConverter] on the enum.
/// </summary>
public sealed class EventTypeConverter : JsonConverter<EventType>
{
    private static readonly string[] WireNames = BuildWireNames();
    private static readonly Dictionary<string, EventType> ByWireName = BuildIndex();

    public override EventType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wireName = reader.GetString() ?? string.Empty;
        return ByWireName.TryGetValue(wireName, out var value) ? value : throw new JsonException($"unknown eventType: {wireName}");
    }

    /// <summary>The TA-5.3 wire name for a value (e.g. "transfer.created") — direct lookup, no serialization round-trip.</summary>
    public static string WireName(EventType value) => WireNames[(int)value];

    public override void Write(Utf8JsonWriter writer, EventType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(WireNames[(int)value]);

    private static string[] BuildWireNames() =>
        typeof(EventType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(fld => fld.IsLiteral)
            .Select(fld => fld.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name!)
            .ToArray();

    private static Dictionary<string, EventType> BuildIndex()
    {
        var index = new Dictionary<string, EventType>();
        foreach (var value in Enum.GetValues(typeof(EventType)))
            index[WireNames[(int)value]] = (EventType)value;
        return index;
    }
}
