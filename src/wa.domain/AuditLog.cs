namespace wa.domain;

/// <summary>An audit log entry. TA-3.2: dbo.AuditLog.</summary>
public class AuditLog
{
    /// <summary>BIGINT IDENTITY(1,1) — server-generated (not client).</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets the actor email of the audit log.</summary>
    public string? ActorEmail { get; set; }
    /// <summary>Gets or sets the action of the audit log.</summary>
    public string Action { get; set; } = null!;
    /// <summary>Gets or sets the entity type of the audit log.</summary>
    public string EntityType { get; set; } = null!;
    /// <summary>Gets or sets the entity ID of the audit log.</summary>
    public string? EntityId { get; set; }
    /// <summary>Gets or sets the details JSON of the audit log.</summary>
    public string? DetailsJson { get; set; }
    /// <summary>Gets or sets the created at UTC of the audit log.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
