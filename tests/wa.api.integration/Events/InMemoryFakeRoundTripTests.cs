using System.Text.Json;
using wa.domain;
using wa.infrastructure.Events;

namespace wa.api.integration.Events;

/// <summary>
/// T-054-07 (AC-054-4, T-006 exit evidence 7/7): the in-memory fake round-trip — publish → store → retrieve by topic.
/// Fake only: constructed directly with an injectable clock, no SB, no Testcontainers (no live broker anywhere).
/// </summary>
public class InMemoryFakeRoundTripTests
{
    // ---- publish → store → retrieve by topic core; no cross-topic contamination ----

    [Fact]
    public async Task PublishAndRetrieveByCore_ReturnsPublishedEventsInOrder_NoOtherTopic()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(CreateEnvelope("11111111-2222-3333-4444-555555555555", EventType.TransferCreated), CancellationToken.None);
        await publisher.PublishAsync(CreateEnvelope("99999999-8888-7777-6666-555555555555", EventType.EmailSent), CancellationToken.None);

        var onCore = publisher.GetByTopic("core");

        Assert.Equal(2, onCore.Count); // publish order preserved
        Assert.Contains("\"eventType\":\"transfer.created\"", onCore[0].Json);
        Assert.Contains("\"eventType\":\"email.sent\"", onCore[1].Json);
        Assert.Empty(publisher.GetByTopic("other-topic")); // no cross-topic contamination — the fake is single-topic by design (TA-5.1 topic core)
    }

    // ---- TTL expiry via the injectable clock: expired entries leave the store, no real waits ----

    [Fact]
    public async Task TtlExpiry_ExpiredEntriesLeaveTheStore()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);
        var publisher = new InMemoryEventPublisher(TimeSpan.FromMinutes(15), () => now);
        await publisher.PublishAsync(CreateEnvelope("11111111-2222-3333-4444-555555555555", EventType.TransferCreated), CancellationToken.None);

        Assert.Single(publisher.GetByTopic("core"));

        now = now.AddMinutes(15); // advance the fake clock past the TTL — no real waits
        var onCoreAfterExpiry = publisher.GetByTopic("core");

        Assert.Empty(onCoreAfterExpiry); // expired entries leave the store
    }

    // ---- round-trip fidelity: read-back is byte-exact TA-5.2 (second Gherkin block) ----

    [Fact]
    public async Task RoundTrip_ReadBackIsByteExactTa52Envelope()
    {
        var publisher = new InMemoryEventPublisher();
        var envelope = CreateEnvelope("11111111-2222-3333-4444-555555555555", EventType.TransferCreated);

        await publisher.PublishAsync(envelope, CancellationToken.None);
        var readBack = publisher.GetByTopic("core");

        Assert.Single(readBack);
        Assert.Equal(
            "{\"eventId\":\"11111111-2222-3333-4444-555555555555\",\"eventType\":\"transfer.created\",\"version\":1,\"occurredAt\":\"2026-10-12T08:30:00Z\",\"correlationId\":\"66666666-7777-8888-9999-000000000000\",\"partitionKey\":\"transfer:22222222\",\"payload\":{\"from\":\"a@b.com\",\"to\":\"c@d.com\"}}",
            readBack[0].Json); // the verified exact TA-5.2 shape — same string SbEventPublisher sends on the wire
    }

    private static EventEnvelope CreateEnvelope(string eventId, EventType type) => new()
    {
        EventId = Guid.Parse(eventId),
        Type = type,
        Version = 1,
        OccurredAtUtc = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc),
        CorrelationId = Guid.Parse("66666666-7777-8888-9999-000000000000"),
        PartitionKey = "transfer:22222222",
        Payload = new { from = "a@b.com", to = "c@d.com" }
    };
}
