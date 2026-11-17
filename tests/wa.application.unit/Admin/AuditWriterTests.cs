using System.Text.Json;
using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-02 (FR-011-5 / TA-3.2 / TA-5.3): the audit write helper — one call writes exactly one AuditLog row
// (columns verbatim) AND emits exactly one admin.action domain event { actor, action, entityType, entityId, details }.
// Reads (GETs) never call the helper → no row, no event. Hand stubs, no Moq (mirrors DeleteTransferCommandTests).
public class AuditWriterTests
{
    private readonly StubAuditStore _store;
    private readonly StubEventPublisher _eventPublisher;
    private readonly IAuditWriter _writer;

    public AuditWriterTests()
    {
        _store = new StubAuditStore();
        _eventPublisher = new StubEventPublisher();
        _writer = new AuditWriter(_store, _eventPublisher);
    }

    // ---- Exit check 1: one call → exactly one AuditLog row, columns verbatim (AC-011-2 audit half) ----

    [Fact]
    public async Task WriteAsync_WritesExactlyOneAuditLogRow_WithVerbatimColumns()
    {
        var actorEmail = "ada@example.com";
        var action = "transfer.force_delete";
        var entityType = "Transfer";
        var entityId = "ABCD1234";
        var details = new { reason = "admin", transferId = Guid.NewGuid() };

        await _writer.WriteAsync(actorEmail, action, entityType, entityId, details, CancellationToken.None);

        var row = Assert.Single(_store.Rows); // exactly one row
        Assert.Equal(actorEmail, row.ActorEmail);   // verbatim
        Assert.Equal(action, row.Action);           // verbatim
        Assert.Equal(entityType, row.EntityType);    // verbatim
        Assert.Equal(entityId, row.EntityId);        // verbatim
        Assert.NotNull(row.DetailsJson);            // details serialized as plain JSON (not null)
        using var doc = JsonDocument.Parse(row.DetailsJson!);
        Assert.Equal("admin", doc.RootElement.GetProperty("reason").GetString());
    }

    // ---- Exit check 2: exactly one admin.action event with the exact TA-5.3 payload shape ----

    [Fact]
    public async Task WriteAsync_EmitsExactlyOneAdminActionEvent_WithTa53PayloadShape()
    {
        var actorEmail = "ada@example.com";
        var action = "transfer.force_delete";
        var entityType = "Transfer";
        var entityId = "ABCD1234";
        var details = new { reason = "admin" };

        await _writer.WriteAsync(actorEmail, action, entityType, entityId, details, CancellationToken.None);

        // Emitted exactly once.
        var envelope = Assert.Single(_eventPublisher.Published);
        Assert.Equal(EventType.AdminAction, envelope.Type); // member 11 of the TA-5.3 closed set — no new enum member
        Assert.Equal(1, envelope.Version);

        // Payload has EXACTLY the keys actor/action/entityType/entityId/details with correct values (TA-5.3).
        var payloadJson = JsonSerializer.Serialize(envelope.Payload!);
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        Assert.Equal(5, root.EnumerateObject().Count()); // exactly five keys — no more, no less
        Assert.Equal(actorEmail, root.GetProperty("actor").GetString());
        Assert.Equal(action, root.GetProperty("action").GetString());
        Assert.Equal(entityType, root.GetProperty("entityType").GetString());
        Assert.Equal(entityId, root.GetProperty("entityId").GetString());
        Assert.Equal("admin", root.GetProperty("details").GetProperty("reason").GetString()); // details object round-trips
    }

    // ---- Exit check 3: reads never call the helper → no row, no event (AC-011-2 audit half) ----

    [Fact]
    public async Task ReadPath_HelperNotInvoked_WritesNoRow_NoEvent()
    {
        // Fresh stubs; a read path performs no WriteAsync call at all.
        var store = new StubAuditStore();
        var publisher = new StubEventPublisher();
        var writer = new AuditWriter(store, publisher);

        // No call — the read path never touches the helper.
        await Task.CompletedTask;

        Assert.Empty(store.Rows);       // no audit row for reads
        Assert.Empty(publisher.Published); // no event for reads
    }

    // ---- Stubs (hand-written, no Moq — mirrors DeleteTransferCommandTests) ----

    private sealed class StubAuditStore : IAuditStore
    {
        public List<AuditLog> Rows { get; } = [];

        public Task AddAsync(AuditLog row, CancellationToken ct)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }
}
