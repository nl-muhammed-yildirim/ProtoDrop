namespace wa.domain;

/// <summary>Magic link token, single-use. TA-3.2: dbo.AuthToken.</summary>
public class AuthToken
{
    /// <summary>Gets or sets the ID of the auth token.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the email of the auth token.</summary>
    public string Email { get; set; } = null!;
    /// <summary>sha256(token), hex (64 chars).</summary>
    public string TokenHash { get; set; } = null!;
    /// <summary>0=signup, 1=login, 2=forgot.</summary>
    public byte Purpose { get; set; }
    /// <summary>Gets or sets the expires at UTC of the auth token.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>Gets or sets the redeemed at UTC of the auth token.</summary>
    public DateTime? RedeemedAtUtc { get; set; }
    /// <summary>Gets or sets the created at UTC of the auth token.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
