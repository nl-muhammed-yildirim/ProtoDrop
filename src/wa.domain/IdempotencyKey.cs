namespace wa.domain;

public class IdempotencyKey
{
    public string Key { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public Guid? TransferId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}