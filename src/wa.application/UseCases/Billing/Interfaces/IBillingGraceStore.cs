namespace wa.application.UseCases.Billing;

/// <summary>US-019-05 (D-4): port for the f-expire billing-grace sub-step. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingGraceStore
{
    /// <summary>Paused subscriptions with GraceEndsAtUtc &lt;= now that have no JobRun claim at that anchor yet.</summary>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching grace candidate items.</returns>
    Task<IReadOnlyList<GraceCandidate>> GetExpiredGracesAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// One transaction: locked re-read (EC-019-3) → JobRun claim (TA-6.1) → AppUser.PlanId = Free.
    /// </summary>
    /// <param name="subscriptionId">Identifier of the subscription.</param>
    /// <param name="graceEndsAtUtc">The grace ends at UTC.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The grace downgrade result.</returns>
    Task<GraceDowngradeResult> TryDowngradeAsync(Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, CancellationToken ct);
}
