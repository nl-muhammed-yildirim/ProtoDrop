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
    /// <param name="actorEmail">The actor email.</param>
    /// <param name="action">The action.</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="entityId">Identifier of the entity.</param>
    /// <param name="details">The details.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task WriteAsync(string actorEmail, string action, string entityType, string? entityId, object? details, CancellationToken ct);
}
