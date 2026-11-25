namespace wa.application.UseCases.Billing;

/// <summary>US-019-05 (D-4): port for the f-expire billing-grace sub-step. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingGraceStore
{
    /// <summary>Finds paused subscriptions whose grace has ended and that have no job-lock claim for that grace end yet.</summary>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The candidates to process; empty when none are due.</returns>
    Task<IReadOnlyList<GraceCandidate>> GetExpiredGracesAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>In one transaction, re-reads the subscription under a row lock, claims the job lock for its grace end and moves the user to the free plan.</summary>
    /// <param name="subscriptionId">The paused subscription to process.</param>
    /// <param name="graceEndsAtUtc">The grace end that was seen when the candidate was listed; the claim anchor.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The outcome, with user and plan details when the user was downgraded.</returns>
    Task<GraceDowngradeResult> TryDowngradeAsync(Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, CancellationToken ct);
}
