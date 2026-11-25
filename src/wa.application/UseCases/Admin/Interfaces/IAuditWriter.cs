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
    /// <param name="actorEmail">Email of the admin who performed the action; PII (TA-9.4).</param>
    /// <param name="action">Short action code, for example the kind of change made.</param>
    /// <param name="entityType">Kind of entity that was changed.</param>
    /// <param name="entityId">Identifier of the changed entity as text; null when there is no single target.</param>
    /// <param name="details">Optional object serialized as JSON into the audit row and the event; must not contain raw PII.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task WriteAsync(string actorEmail, string action, string entityType, string? entityId, object? details, CancellationToken ct);
}
