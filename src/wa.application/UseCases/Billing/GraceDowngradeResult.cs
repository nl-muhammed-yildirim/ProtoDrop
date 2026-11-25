namespace wa.application.UseCases.Billing;

/// <summary>User fields are set only for Downgraded (email is never logged, TA-9.4).</summary>
/// <param name="Outcome">The outcome of the grace downgrade result.</param>
/// <param name="UserId">Identifier of the user of the grace downgrade result.</param>
/// <param name="Email">The email of the grace downgrade result.</param>
/// <param name="FromPlanCode">The from plan code of the grace downgrade result.</param>
/// <param name="ToPlanCode">The to plan code of the grace downgrade result.</param>
public sealed record GraceDowngradeResult(
    GraceDowngradeOutcome Outcome, Guid UserId = default, string? Email = null, string? FromPlanCode = null, string? ToPlanCode = null);
