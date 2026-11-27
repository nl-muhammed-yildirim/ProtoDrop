namespace wa.domain;

/// <summary>An application user. TA-3.2: dbo.AppUser.</summary>
public class AppUser
{
    /// <summary>Primary key of the user.</summary>
    public Guid Id { get; set; }
    /// <summary>Sign-in email, stored lowercased; PII (TA-9.4).</summary>
    public string Email { get; set; } = null!;
    /// <summary>True once the user has redeemed a magic link for this address.</summary>
    public bool EmailConfirmed { get; set; }
    /// <summary>Name shown to recipients as the sender; PII (TA-9.4).</summary>
    public string DisplayName { get; set; } = null!;
    /// <summary>PBKDF2 hash (ASP.NET Identity PasswordHasher); null for magic-link-only accounts.</summary>
    public string? PasswordHash { get; set; }
    /// <summary>The user's current plan (foreign key to the plan definition).</summary>
    public Guid PlanId { get; set; }
    /// <summary>Stored UI theme preference (light or dark).</summary>
    public string Theme { get; set; } = null!;
    /// <summary>UTC instant the account was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Soft-delete marker (TA-3.1): UTC instant the account was deleted, null while active.</summary>
    public DateTime? DeletedAtUtc { get; set; }
    /// <summary>
    /// TA-9.1 session epoch: session tokens issued before this UTC instant are rejected (set when an unconfirmed account is confirmed, so a session
    /// minted at a pre-registration signup dies). Null = no session was ever revoked.
    /// </summary>
    public DateTime? SessionsValidAfterUtc { get; set; }
    /// <summary>F-PRF-001 FR-021-1: organization shown beside the sender name; at most 60 chars. PII (TA-9.4).</summary>
    public string? OrgName { get; set; }
    /// <summary>F-PRF-001 FR-021-1: ORG_NAME_MAX_LENGTH — the NVARCHAR width of <see cref="OrgName"/>.</summary>
    public const int OrgNameMaxLength = 60;
    /// <summary>F-PRF-001: blob path of the org logo (TA-3.5 avatars/{userId}/logo); set by US-021-04.</summary>
    public string? OrgLogoBlobPath { get; set; }

    /// <summary>F-XCT-003 FR-046-1: owner notification emails. Null = never touched = on; false = off; true = explicitly on.</summary>
    public bool? EmailNotificationsEnabled { get; set; }

    /// <summary>FR-046-2: the effective setting — null means on. The single NULL→true rule (GET, f-email gate).</summary>
    /// <param name="stored">Persisted <see cref="EmailNotificationsEnabled"/> value; null means the user never changed it.</param>
    /// <returns><see langword="true"/> when the stored value is true or null; <see langword="false"/> only when explicitly turned off.</returns>
    public static bool EmailNotificationsOn(bool? stored) => stored ?? true;
}
