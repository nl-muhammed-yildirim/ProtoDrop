using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Testcontainers.MsSql;
using wa.application.Events;
using wa.domain;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-006d / T-006 exit check ("Integration test: publish → consume with fake;
/// dedup on replay"). Exercises the event backbone end-to-end at the
/// <see cref="IEventPublisher"/> boundary (TA-5.1/TA-5.2):
///
///   (a) publish → consume — one event through the TA-5.2 in-memory fake is
///       consumed and the fake consumer receives the EXACT TA-5.2 envelope,
///       all seven wire fields.
///   (b) dedup on replay — the same <c>eventId</c> delivered twice (at-least-once)
///       is processed ONCE, via the TA-5.2 "15-min LRU" <see cref="EventDeduplicator"/>.
///   (c) outbox — the real <see cref="SbEventPublisher"/> adapter writing
///       against a live MSSQL container (Testcontainers) leaves the
///       <see cref="EventOutbox"/> row with the matching <c>eventId</c>
///       (the dedup/retry key per TA-5.1).
///
/// (a)/(b) run against the in-memory fake bus (no SQL needed); (c) runs the EF
/// outbox write on a disposable MSSQL container, per the existing
/// <see cref="SeedDataIntegrationTest"/> pattern.
/// </summary>
public class EventBackboneIntegrationTest
{
    // TA-5.2 example values.
    const string TransferId = "11111111-1111-4111-8111-111111111111";
    const string EventId = "6f1a9d4e-3b2c-4a1f-9d8e-1a2b3c4d5e6f";

    [Fact]
    public async Task PublishAndConsume_FakeReceivesExactTa52Envelope()
    {
        // Arrange — one TA-5.3 `transfer.created` event.
        var payload = new TransferCreatedPayload
        {
            TransferId = TransferId,
            LinkId = "abcd1234",
            TotalBytes = 1024,
            FileCount = 1,
            Emails = new List<string> { "recv@example.com" },
            SenderEmail = "sender@example.com",
        };

        var envelope = new EventEnvelope
        {
            EventId = EventId,
            EventType = "transfer.created",
            Version = 1,
            OccurredAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            CorrelationId = "08a1f3c2",
            PartitionKey = TransferId,
            Payload = payload,
        };

        // The TA-5.2 "in-memory fake" stands in for the SB adapter.
        var publisher = new InMemoryEventPublisher();
        var consumer = new FakeConsumer();

        // Act — one event through the adapter, then consume the bus.
        await publisher.PublishAsync(envelope);
        foreach (var delivered in publisher.Published)
        {
            consumer.OnMessage(delivered);
        }

        // Assert — the consumer received EXACTLY one envelope, all 7 fields.
        Assert.Single(consumer.Processed);
        var got = consumer.Processed[0];
        Assert.Equal(envelope.EventId, got.EventId);
        Assert.Equal(envelope.EventType, got.EventType);
        Assert.Equal(envelope.Version, got.Version);
        Assert.Equal(envelope.OccurredAt, got.OccurredAt);
        Assert.Equal(envelope.CorrelationId, got.CorrelationId);
        Assert.Equal(envelope.PartitionKey, got.PartitionKey);
        Assert.Equal(
            JsonSerializer.Serialize(payload),
            JsonSerializer.Serialize(got.Payload));

        // The wire shape is the TA-5.2 JSON verbatim (camelCase keys, all 7).
        var wire = JsonSerializer.Serialize(got);
        Assert.Contains("\"eventId\"", wire);
        Assert.Contains("\"eventType\"", wire);
        Assert.Contains("\"version\"", wire);
        Assert.Contains("\"occurredAt\"", wire);
        Assert.Contains("\"correlationId\"", wire);
        Assert.Contains("\"partitionKey\"", wire);
        Assert.Contains("\"payload\"", wire);
    }

    [Fact]
    public async Task DedupOnReplay_SameEventId_ProcessedOnce()
    {
        var envelope = new EventEnvelope
        {
            EventId = EventId,
            EventType = "transfer.created",
            Version = 1,
            OccurredAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            PartitionKey = TransferId,
            Payload = new TransferCreatedPayload { TransferId = TransferId },
        };

        var publisher = new InMemoryEventPublisher();
        var consumer = new FakeConsumer();

        // Act — at-least-once delivery: the same eventId is published twice.
        await publisher.PublishAsync(envelope);
        await publisher.PublishAsync(envelope); // replay
        foreach (var delivered in publisher.Published)
        {
            consumer.OnMessage(delivered);
        }

        // Assert — TA-5.2 "consumers must be idempotent": one delivery of the
        // eventId is processed, even though the at-least-once bus redelivered
        // the same envelope.
        Assert.Equal(2, consumer.TotalMessages); // both deliveries were seen…
        Assert.Single(consumer.Processed);        // …but only one passed dedup.
    }

    [Fact]
    public async Task Outbox_Publish_HoldsRowWithMatchingEventId()
    {
        await using var sql = Builder().Build();
        await sql.StartAsync();
        await using (var ctx = New(sql))
            await ctx.Database.MigrateAsync();

        using var db = New(sql);

        // Real SB adapter, no ServiceBusClient (local no-op publish per
        // AGENT.md §5.2) — the EF outbox write is what we assert.
        var publisher = new SbEventPublisher(
            db,
            client: null,
            new ServiceBusTopicOptions("core"),
            NullLogger<SbEventPublisher>.Instance);

        var outboxEventId = "7e2b0c5f-4c3d-4b2a-8e7f-2b3c4d5e6f7a";
        var envelope = new EventEnvelope
        {
            EventId = outboxEventId,
            EventType = "transfer.created",
            Version = 1,
            OccurredAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            CorrelationId = "08a1f3c2",
            PartitionKey = TransferId,
            Payload = new TransferCreatedPayload
            {
                TransferId = TransferId,
                LinkId = "abcd1234",
                TotalBytes = 2048,
                FileCount = 1,
                Emails = new List<string>(),
            },
        };

        var rowsBefore = await db.EventOutboxes.CountAsync();

        // Act — publish through the real adapter (writes the outbox row).
        await publisher.PublishAsync(envelope);

        // Assert — exactly one new outbox row, keyed by the matching eventId.
        var rowsAfter = await db.EventOutboxes.CountAsync();
        Assert.Equal(rowsBefore + 1, rowsAfter);

        var row = await db.EventOutboxes
            .AsNoTracking()
            .SingleAsync(b => b.EventId == outboxEventId);

        Assert.Equal(envelope.EventType, row.EventType);
        Assert.Equal(envelope.PartitionKey, row.PartitionKey);
        Assert.Equal(envelope.CorrelationId, row.CorrelationId);
        Assert.False(string.IsNullOrEmpty(row.PayloadJson));
        Assert.NotNull(row.SentAtUtc); // publish completed (no-op client).
    }

    static MsSqlBuilder Builder() =>
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithDatabase("wa_test")
            .WithPassword("YourStrong!Passw0rd");

    static WaDbContext New(MsSqlContainer sql)
        => new(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(sql.GetConnectionString())
            .Options);

    /// <summary>
    /// FAKE consumer (stand-in for a real subscription consumer, e.g.
    /// <c>f-email</c>). Receives each delivery off the (fake) bus and guards
    /// the handler with the TA-5.2 <see cref="EventDeduplicator"/> ("consumers
    /// must be idempotent — dedup by eventId against a 15-min LRU").
    /// </summary>
    sealed class FakeConsumer
    {
        private readonly EventDeduplicator _dedup = new();

        public int TotalMessages { get; private set; }

        public List<EventEnvelope> Processed { get; } = new();

        /// <summary>One at-least-once message delivery off the fake bus.</summary>
        public void OnMessage(EventEnvelope envelope)
        {
            TotalMessages++;
            // TA-5.2: run the work only on first sighting of the eventId.
            if (_dedup.TryProcess(envelope.EventId))
            {
                Processed.Add(envelope);
            }
        }
    }

    /// <summary>
    /// TA-5.3 <c>transfer.created</c> payload shape — the typed box that
    /// <see cref="EventEnvelope.Payload"/> carries for that event type.
    /// </summary>
    sealed record TransferCreatedPayload
    {
        public string TransferId { get; init; } = string.Empty;
        public string LinkId { get; init; } = string.Empty;
        public long TotalBytes { get; init; }
        public int FileCount { get; init; }
        public List<string> Emails { get; init; } = new();
        public string? SenderEmail { get; init; }
    }
}