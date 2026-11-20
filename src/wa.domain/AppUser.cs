namespace wa.domain;

/// <summary>An application user. TA-3.2: dbo.AppUser.</summary>
public class AppUser
{
    public Guid Id { get; set; }
    /// <summary>Stored lowercased.</summary>
    public string Email { get; set; } = null!;
    public bool EmailConfirmed { get; set; }
    public string DisplayName { get; set; } = null!;
    /// <summary>PBKDF2 (ASP.NET Identity PasswordHasher).</summary>
    public string? PasswordHash { get; set; }
    public Guid PlanId { get; set; }
    public string Theme { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Soft-delete (TA-3.1).</summary>
    public DateTime? DeletedAtUtc { get; set; }
    /// <summary>F-PRF-001 FR-021-1: organization shown beside the sender name; at most 60 chars. PII (TA-9.4).</summary>
    public string? OrgName { get; set; }
    /// <summary>F-PRF-001 FR-021-1: ORG_NAME_MAX_LENGTH — the NVARCHAR width of <see cref="OrgName"/>.</summary>
    public const int OrgNameMaxLength = 60;
    /// <summary>F-PRF-001: blob path of the org logo (TA-3.5 avatars/{userId}/logo); set by US-021-04.</summary>
    public string? OrgLogoBlobPath { get; set; }
}
