namespace wa.application.UseCases.Billing;

/// <summary>Result of one grace downgrade attempt. The user fields are set only for <see cref="GraceDowngradeOutcome.Downgraded"/> (the email is never logged, TA-9.4).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The downgraded user; default when nothing was downgraded.</param>
/// <param name="Email">Address for the grace-ended email; PII (TA-9.4).</param>
/// <param name="FromPlanCode">Plan code before the downgrade.</param>
/// <param name="ToPlanCode">Plan code after the downgrade (the free plan).</param>
public sealed record GraceDowngradeResult(
    GraceDowngradeOutcome Outcome, Guid UserId = default, string? Email = null, string? FromPlanCode = null, string? ToPlanCode = null);
