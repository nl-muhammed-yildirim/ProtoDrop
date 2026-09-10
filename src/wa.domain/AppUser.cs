namespace wa.domain;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public Guid PlanId { get; set; }
    public string Theme { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}