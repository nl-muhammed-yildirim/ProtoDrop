namespace wa.domain;

public class AuditLog
{
    public long Id { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
