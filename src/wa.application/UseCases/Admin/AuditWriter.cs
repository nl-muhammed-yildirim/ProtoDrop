using System.Text.Json;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-02 (FR-011-5 / TA-3.2 / TA-5.3): the audit write helper — one call writes exactly one AuditLog row
/// (ActorEmail, Action, EntityType, EntityId, DetailsJson verbatim) AND emits exactly one admin.action domain
/// event { actor, action, entityType, entityId, details } in the same request. Actor email is passed explicitly
/// so commands stay thin (TA-4.2a). Reads (GETs) never call this — only mutations do.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Writes one AuditLog row + emits one admin.action event. details is serialized as plain JSON into DetailsJson
    /// (null when null); the event payload exposes exactly actor/action/entityType/entityId/details.
    /// </summary>
    Task WriteAsync(string actorEmail, string action, string entityType, string? entityId, object? details, CancellationToken ct);
}

/// <summary>
/// T-011-02: AuditWriter — the only implementation of IAuditWriter. Mirrors DeleteTransferCommand's event idiom
/// (EventEnvelope { EventId=Guid.NewGuid(), Type, Version=1, OccurredAtUtc, CorrelationId, PartitionKey=row identity, Payload }
/// then IEventPublisher.PublishAsync).
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditStore _store;
    private readonly IEventPublisher _eventPublisher;

    public AuditWriter(IAuditStore store, IEventPublisher eventPublisher)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public async Task WriteAsync(string actorEmail, string action, string entityType, string? entityId, object? details, CancellationToken ct)
    {
        // 1. AuditLog row — columns verbatim (TA-3.2); Id is BIGINT IDENTITY server-gen; CreatedAtUtc = now.
        var row = new AuditLog
        {
            ActorEmail = actorEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            CreatedAtUtc = DateTime.UtcNow
        };
        await _store.AddAsync(row, ct);

        // 2. admin.action domain event — TA-5.3 closed set (EventType.AdminAction already exists; no new member).
        var payload = new
        {
            actor = actorEmail,
            action,
            entityType,
            entityId,
            details
        };

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.AdminAction,   // member 11 of the TA-5.3 closed set — no new enum member
            Version = 1,
            OccurredAtUtc = row.CreatedAtUtc,
            CorrelationId = GetCorrelationGuid(),
            PartitionKey = entityId ?? entityType, // DECISION: per-entity routing keeps same-entity actions ordered
            Payload = payload
        };

        await _eventPublisher.PublishAsync(envelope, ct);
    }

    /// <summary>
    /// Derives the correlation Guid from the W3C trace-id (32 hex = 16 bytes). Falls back to a fresh Guid outside a request.
    /// Mirrors DeleteTransferCommand.GetCorrelationGuid() verbatim.
    /// </summary>
    private static Guid GetCorrelationGuid()
    {
        var traceId = CorrelationContext.CurrentRequest?.TraceId;
        if (traceId is not null && traceId.Length == 32)
        {
            var hex = traceId.Replace("-", "");
            if (hex.Length == 32 && System.Linq.Enumerable.All(hex, c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return new Guid(hex);
        }
        return Guid.NewGuid();
    }
}
