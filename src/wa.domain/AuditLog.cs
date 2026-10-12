namespace wa.domain;

/// <summary>An audit log entry. TA-3.2: dbo.AuditLog.</summary>
public class AuditLog
{
    /// <summary>BIGINT IDENTITY(1,1) — server-generated (not client).</summary>
    public long Id { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
