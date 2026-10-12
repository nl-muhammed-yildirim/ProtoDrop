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
}
