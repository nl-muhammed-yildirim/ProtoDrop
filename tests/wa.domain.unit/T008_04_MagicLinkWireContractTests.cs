using wa.domain;
using wa.domain.Errors;

namespace wa.domain.unit;

/// <summary>
/// T-008-04 (TA-9.1 / D-6): EventType is a closed list (12 members at M0) — the new magic-link wire name
/// round-trips exactly, and LINK_EXPIRED maps 410 with the frozen title.
/// </summary>
public class T008_04_MagicLinkWireContractTests
{
    [Fact]
    public void EventType_MagicLinkRequested_UsesFrozenWireName()
    {
        // D-6: wire name is auth.magic_link_requested — TA-5.3 closed list, 12th member.
        Assert.Equal("auth.magic_link_requested", EventTypeConverter.WireName(EventType.MagicLinkRequested));

        // Round-trip through the converter (byte-exact, no naming policy).
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new EventEnvelope
            {
                EventId = Guid.NewGuid(),
                Type = EventType.MagicLinkRequested,
                PartitionKey = "user@example.com"
            }));
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("\"eventType\":\"auth.magic_link_requested\"", json);
    }

    [Fact]
    public void EventType_MagicLinkRequested_RoundTripsThroughJson()
    {
        var original = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.MagicLinkRequested,
            Version = 1,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            PartitionKey = "user@example.com",
            Payload = new { email = "user@example.com", token = "raw", expiresAtUtc = DateTime.UtcNow.AddMinutes(10) }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<EventEnvelope>(json)!;

        Assert.Equal(EventType.MagicLinkRequested, restored.Type);
    }

    [Fact]
    public void ProblemCodes_LinkExpired_Is410WithFrozenTitle()
    {
        // EC-008-3: expired (10 min) ≡ redeemed — identical state via the same code. Don't leak which.
        Assert.Equal(410, ProblemCodes.StatusOf(ProblemCodes.LinkExpired));
        Assert.Equal("This link has expired", ProblemCodes.TitleOf(ProblemCodes.LinkExpired));
    }

    [Fact]
    public void ProblemCodes_LinkExpired_IsInClosedCodeList()
    {
        // TA-4.1.3: every status comes from the closed code list — LINK_EXPIRED is now the 18th member.
        Assert.Equal(ProblemCodes.LinkExpired, "LINK_EXPIRED");

        // Round-trip through the Table: StatusOf + TitleOf both resolve without throwing.
        var status = ProblemCodes.StatusOf(ProblemCodes.LinkExpired);
        var title = ProblemCodes.TitleOf(ProblemCodes.LinkExpired);

        Assert.Equal(410, status);
        Assert.False(string.IsNullOrEmpty(title));
    }
}
