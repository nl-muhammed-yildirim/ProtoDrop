namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: result of the one-transaction dedup + mirror upsert (D-3).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="UserId">The user the subscription belongs to; empty unless applied.</param>
/// <param name="FromPlanId">Plan id before the change.</param>
/// <param name="ToPlanId">Plan id after the change.</param>
/// <param name="FromPlanCode">Plan code before the change.</param>
/// <param name="ToPlanCode">Plan code after the change.</param>
public sealed record StripeApplyResult(
    StripeApplyOutcome Outcome,
    Guid UserId,
    Guid FromPlanId,
    Guid ToPlanId,
    string FromPlanCode,
    string ToPlanCode)
{
    /// <summary>Creates the stripe apply result of.</summary>
    /// <param name="outcome">The outcome to report.</param>
    /// <returns>A result with that outcome and empty ids and codes.</returns>
    public static StripeApplyResult Of(StripeApplyOutcome outcome) =>
        new(outcome, Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty);
}
