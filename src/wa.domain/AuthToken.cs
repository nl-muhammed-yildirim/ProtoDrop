namespace wa.domain;

public class AuthToken
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public byte Purpose { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}