using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration.Events;

/// <summary>
/// T-054-06 group 2 (T-006b, EC-054-1): outbox write-on-change + idempotent retry — fakes only (no live broker).
/// The in-memory fake mirrors broker-level MessageId dedup; the real-SQL half pins that a failed publish leaves the row
/// unpublished and that no duplicate message is possible: PK = EventId, so a second insert of the same envelope cannot exist.
/// </summary>
public class OutboxRetryTests : IAsyncLifetime
{
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await container.StartAsync();

        // master is the SQL Server's default DB — migrations create the schema there; nothing else to drop.
        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(container.GetConnectionString())
            .Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ---- Idempotent retry: same eventId twice → one stored message, no double side effect (MessageId dedup) ----

    [Fact]
    public async Task RePublish_SameEventId_LeavesExactlyOneStoredEnvelopeWithExactJson()
    {
        var publisher = new InMemoryEventPublisher(); // fake is the broker stand-in — same contract as SbEventPublisher
        var envelope = CreateEnvelope();

        var first = await publisher.PublishAsync(envelope, CancellationToken.None);
        var second = await publisher.PublishAsync(envelope, CancellationToken.None);

        Assert.True(first.IsPublished);
        Assert.True(second.IsPublished); // dedup: the retry is a no-op side effect-wise
        Assert.Equal(JsonSerializer.Serialize(envelope), Assert.Single(publisher.GetByTopic("core")).Json);
    }

    private static EventEnvelope CreateEnvelope() => new()
    {
        EventId = Guid.NewGuid(),
        Type = EventType.TransferCreated,
        Version = 1,
        OccurredAtUtc = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid(),
        PartitionKey = "transfer:22222222",
        Payload = new { from = "a@b.com", to = "c@d.com" }
    };

    // ---- EC-054-1: failed publish leaves the row unpublished; PK on EventId makes a duplicate message impossible ----

    [Fact]
    public async Task FailedPublish_LeavesRowUnpublished_And_SameEventIdCannotCreateASecondRow()
    {
        var connString = _db.Database.GetConnectionString();
        // Empty configuration: no Wa:ServiceBus:ConnectionString → ServiceBusClient ctor throws inside the adapter's try.
        var publisher = new SbEventPublisher(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SbEventPublisher>.Instance,
            new AppDbContextFactory(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options));
        var envelope = CreateEnvelope();

        // First publish: row committed via the change tracker, broker unreachable → stays unpublished.
        var result = await publisher.PublishAsync(envelope, CancellationToken.None);

        Assert.False(result.IsPublished);
        var row = await _db.Set<DomainEvent>().FirstAsync(e => e.EventId == envelope.EventId);
        Assert.False(row.IsPublished);
        Assert.Equal(JsonSerializer.Serialize(envelope), row.EnvelopeJson); // byte-exact TA-5.2 — re-publish source

        // Re-publish of the same eventId: PK on EventId rejects a second row → no duplicate message at DB level.
        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => publisher.PublishAsync(envelope, CancellationToken.None));
        Assert.Equal(1, await _db.Set<DomainEvent>().CountAsync(e => e.EventId == envelope.EventId));
    }
}
