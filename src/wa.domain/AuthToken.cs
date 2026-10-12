namespace wa.domain;

/// <summary>Magic link token, single-use. TA-3.2: dbo.AuthToken.</summary>
public class AuthToken
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    /// <summary>sha256(token), hex (64 chars).</summary>
    public string TokenHash { get; set; } = null!;
    /// <summary>0=signup, 1=login, 2=forgot.</summary>
    public byte Purpose { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
