using wa.domain.Privacy;

namespace wa.domain;

/// <summary>Magic link token, single-use. TA-3.2: dbo.AuthToken.</summary>
public class AuthToken
{
    /// <summary>Primary key of the token row.</summary>
    public Guid Id { get; set; }
    /// <summary>Address the magic link was issued for (normalized lowercase); PII (TA-9.4).</summary>
    [Pii]
    public string Email { get; set; } = null!;
    /// <summary>sha256(token), hex (64 chars).</summary>
    public string TokenHash { get; set; } = null!;
    /// <summary>0=signup, 1=login, 2=forgot.</summary>
    public byte Purpose { get; set; }
    /// <summary>UTC instant after which the token can no longer be redeemed.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>UTC instant the token was redeemed; null while unused. A redeemed token cannot be reused.</summary>
    public DateTime? RedeemedAtUtc { get; set; }
    /// <summary>UTC instant the token was issued.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
