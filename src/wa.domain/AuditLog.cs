using wa.domain.Privacy;

namespace wa.domain;

/// <summary>An audit log entry. TA-3.2: dbo.AuditLog.</summary>
public class AuditLog
{
    /// <summary>BIGINT IDENTITY(1,1) key, server-generated (not client).</summary>
    public long Id { get; set; }
    /// <summary>Email of the admin or system actor that performed the action; null for unattended jobs.</summary>
    [Pii]
    public string? ActorEmail { get; set; }
    /// <summary>Short action code describing what was done (for example an erasure or a plan change).</summary>
    public string Action { get; set; } = null!;
    /// <summary>Kind of entity the action touched (for example user or transfer).</summary>
    public string EntityType { get; set; } = null!;
    /// <summary>Identifier of the touched entity as text; null when the action had no single target.</summary>
    public string? EntityId { get; set; }
    /// <summary>Optional JSON payload with action-specific details; may carry a raw address for user/suppression actions, so it is PII (TA-9.4).</summary>
    [Pii]
    public string? DetailsJson { get; set; }
    /// <summary>UTC instant the entry was written.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
