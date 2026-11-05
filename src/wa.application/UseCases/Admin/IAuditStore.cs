using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-02 (FR-011-5 / TA-3.2): the audit row's only DB surface — one bounded insert per call.
/// Implemented by <c>wa.infrastructure.Persistence.AuditStore</c>, registered as a singleton in Program.cs
/// (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public interface IAuditStore
{
    /// <summary>Inserts exactly one AuditLog row (ActorEmail, Action, EntityType, EntityId, DetailsJson verbatim; Id server-generated).</summary>
    Task AddAsync(AuditLog row, CancellationToken ct);
}
