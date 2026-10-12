using System.Text.Json;
using wa.domain;

namespace wa.domain.unit.Events;

/// <summary>
/// T-054-06 group 1 (AC-054-2): the TA-5.2 envelope shape pinned byte-exact for EVERY type in the closed TA-5.3 list —
/// exactly the seven fields, nothing else; anything else fails CI. Plain <c>JsonSerializer</c>, no options: key names are
/// pinned by [JsonPropertyName] and EventType's bound converter emits its TA-5.3 wire name (the dotted form no naming policy produces).
/// </summary>
public class EnvelopeShapeTests
{
    private const string EventId = "11111111-2222-3333-4444-555555555555";
    private const string CorrelationId = "66666666-7777-8888-9999-000000000000";
    private const string OccurredAt = "2026-10-12T08:30:00Z";
    private const string PartitionKey = "transfer:22222222";

    // ---- AC-054-2: exact full-string equality for every TA-5.3 type — all 11 members of the closed list, one pass each ----

    [Fact]
    public void EventEnvelope_SerializesToExactTa52Shape_ForEveryTa53Type()
    {
        var ta53Types = new (EventType Type, string WireName)[]
        {
            (EventType.TransferCreated, "transfer.created"),
            (EventType.TransferExpired, "transfer.expired"),
            (EventType.TransferDeleted, "transfer.deleted"),
            (EventType.EmailSent, "email.sent"),
            (EventType.EmailFailed, "email.failed"),
            (EventType.DownloadCompleted, "download.completed"),
            (EventType.ZipGenerated, "zip.generated"),
            (EventType.UserCreated, "user.created"),
            (EventType.AccountDeleted, "account.deleted"),
            (EventType.PlanChanged, "plan.changed"),
            (EventType.AdminAction, "admin.action")
        };

        // The closed TA-5.3 list is exactly these 11 members — anything else fails CI.
        Assert.Equal(11, ta53Types.Length);
        Assert.Equal(ta53Types.Select(t => t.Type), Enum.GetValues<EventType>());

        foreach (var (type, wireName) in ta53Types)
            Assert.Equal(ExpectedEnvelope(wireName), JsonSerializer.Serialize(CreateEnvelope(type)));
    }

    // ---- AC-054-2: independent structural check — key sequence is exactly the seven fields, in order, nothing else ----

    [Fact]
    public void EventEnvelope_SerializedKeySequence_IsExactlyTheSevenTa52FieldsInOrder()
    {
        var actual = JsonSerializer.Serialize(CreateEnvelope(EventType.TransferCreated));

        using var doc = JsonDocument.Parse(actual);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(
            new[] { "eventId", "eventType", "version", "occurredAt", "correlationId", "partitionKey", "payload" },
            keys);
    }

    private static string ExpectedEnvelope(string wireName) =>
        "{\"eventId\":\"" + EventId + "\",\"eventType\":\"" + wireName + "\",\"version\":1,\"occurredAt\":\"" + OccurredAt
        + "\",\"correlationId\":\"" + CorrelationId + "\",\"partitionKey\":\"" + PartitionKey
        + "\",\"payload\":{\"from\":\"a@b.com\",\"to\":\"c@d.com\"}}";

    private static EventEnvelope CreateEnvelope(EventType type) => new()
    {
        EventId = Guid.Parse(EventId),
        Type = type,
        Version = 1,
        OccurredAtUtc = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc),
        CorrelationId = Guid.Parse(CorrelationId),
        PartitionKey = PartitionKey,
        Payload = new { from = "a@b.com", to = "c@d.com" }
    };
}
