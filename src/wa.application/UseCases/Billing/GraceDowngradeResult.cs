namespace wa.application.UseCases.Billing;

/// <summary>User fields are set only for Downgraded (email is never logged, TA-9.4).</summary>
public sealed record GraceDowngradeResult(
    GraceDowngradeOutcome Outcome, Guid UserId = default, string? Email = null, string? FromPlanCode = null, string? ToPlanCode = null);
