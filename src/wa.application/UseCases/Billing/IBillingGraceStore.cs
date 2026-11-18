namespace wa.application.UseCases.Billing;

/// <summary>One paused subscription whose grace has ended and has not been claimed yet.</summary>
public sealed record GraceCandidate(Guid SubscriptionId, DateTime GraceEndsAtUtc);

public enum GraceDowngradeOutcome { Downgraded, NotDue, AlreadyClaimed, NoChange }

/// <summary>User fields are set only for Downgraded (email is never logged, TA-9.4).</summary>
public sealed record GraceDowngradeResult(
    GraceDowngradeOutcome Outcome, Guid UserId = default, string? Email = null, string? FromPlanCode = null, string? ToPlanCode = null);

/// <summary>US-019-05 (D-4): port for the f-expire billing-grace sub-step. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingGraceStore
{
    /// <summary>Paused subscriptions with GraceEndsAtUtc &lt;= now that have no JobRun claim at that anchor yet.</summary>
    Task<IReadOnlyList<GraceCandidate>> GetExpiredGracesAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// One transaction: locked re-read (EC-019-3) → JobRun claim (TA-6.1) → AppUser.PlanId = Free.
    /// </summary>
    Task<GraceDowngradeResult> TryDowngradeAsync(Guid subscriptionId, DateTime graceEndsAtUtc, DateTime nowUtc, CancellationToken ct);
}
