namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: result of the one-transaction dedup + mirror upsert (D-3).</summary>
public sealed record StripeApplyResult(
    StripeApplyOutcome Outcome,
    Guid UserId,
    Guid FromPlanId,
    Guid ToPlanId,
    string FromPlanCode,
    string ToPlanCode)
{
    public static StripeApplyResult Of(StripeApplyOutcome outcome) =>
        new(outcome, Guid.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty);
}
