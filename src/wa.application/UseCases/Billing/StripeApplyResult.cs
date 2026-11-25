namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: result of the one-transaction dedup + mirror upsert (D-3).</summary>
/// <param name="Outcome">The outcome of the stripe apply result.</param>
/// <param name="UserId">Identifier of the user of the stripe apply result.</param>
/// <param name="FromPlanId">Identifier of the from plan of the stripe apply result.</param>
/// <param name="ToPlanId">Identifier of the to plan of the stripe apply result.</param>
/// <param name="FromPlanCode">The from plan code of the stripe apply result.</param>
/// <param name="ToPlanCode">The to plan code of the stripe apply result.</param>
public sealed record StripeApplyResult(
    StripeApplyOutcome Outcome,
    Guid UserId,
    Guid FromPlanId,
    Guid ToPlanId,
    string FromPlanCode,
    string ToPlanCode)
{
    /// <summary>Creates the stripe apply result of.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The stripe apply result.</returns>
    public static StripeApplyResult Of(StripeApplyOutcome outcome) =>
        new(outcome, Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty);
}
