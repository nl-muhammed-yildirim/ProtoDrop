using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>
/// Serializes <see cref="EventType"/> as its TA-5.3 wire name (e.g. "transfer.created") — the dotted form no naming policy can produce from member names, so plain <c>JsonSerializer</c> calls stay option-free. Bound via [JsonConverter] on the enum.
/// </summary>
public sealed class EventTypeConverter : JsonConverter<EventType>
{
    private static readonly string[] WireNames = BuildWireNames();
    private static readonly Dictionary<string, EventType> ByWireName = BuildIndex();

    /// <inheritdoc />
    public override EventType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wireName = reader.GetString() ?? string.Empty;
        return ByWireName.TryGetValue(wireName, out var value) ? value : throw new JsonException($"unknown eventType: {wireName}");
    }

    /// <summary>The TA-5.3 wire name for a value (e.g. "transfer.created") — direct lookup, no serialization round-trip.</summary>
    /// <param name="value">The event type to name.</param>
    /// <returns>The dotted TA-5.3 wire name, for example <c>transfer.created</c>.</returns>
    public static string WireName(EventType value) => WireNames[(int)value];

    /// <inheritdoc />
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
        foreach (var value in Enum.GetValues<EventType>())
            index[WireNames[(int)value]] = value;
        return index;
    }
}
