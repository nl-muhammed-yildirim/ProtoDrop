using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-05 (D-1, D-2, FR-019-6): pure rules for the 14-day billing grace after customer.subscription.paused.
/// BCL + wa.domain only — no clock, no I/O. The 14 days is a spec constant for all plans (FR-019-6, TA-7.5 step 3),
/// not a Limits Registry value.
/// </summary>
public static class BillingGrace
{
    /// <summary>FR-019-6: grace length, 14 days for all plans.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromDays(14);

    /// <summary>Email kind string for the grace-ended notification (<c>billing_grace_ended</c>).</summary>
    public const string GraceEndedKind = "billing_grace_ended";
    /// <summary>Stripe subscription status that starts the grace period (<c>paused</c>).</summary>
    public const string PausedStatus = "paused";
    /// <summary>Reason value on the <c>plan.changed</c> event when a plan is downgraded because grace ended.</summary>
    public const string GraceEndedReason = "grace_ended";

    /// <summary>US-019-06 (FR-019-8): plan.changed reason when customer.subscription.resumed changes the plan.</summary>
    public const string ResumedReason = "resumed";

    /// <summary>
    /// The grace end after a status change. A pause starts grace (now + 14 d) unless the row was already paused with a
    /// grace set (a replayed or second paused/updated event never extends it). Any other status clears it
    /// (US-019-06: grace only has meaning while paused, so resumed/updated(active) in either order clears it).
    /// </summary>
    /// <param name="previousStatus">Subscription status before this update; null for a new row.</param>
    /// <param name="currentGraceEndsAtUtc">Grace end currently stored on the row, if any.</param>
    /// <param name="newStatus">Subscription status reported by Stripe.</param>
    /// <param name="nowUtc">Current UTC time; the base of a newly started grace.</param>
    /// <returns>The grace end to store: the existing one for a repeated pause, now plus <see cref="Duration"/> for a new pause, or <see langword="null"/> for any other status.</returns>
    public static DateTime? NextGraceEndsAt(string? previousStatus, DateTime? currentGraceEndsAtUtc, string newStatus, DateTime nowUtc)
    {
        if (newStatus != PausedStatus)
            return null;

        if (previousStatus == PausedStatus && currentGraceEndsAtUtc is not null)
            return currentGraceEndsAtUtc;

        return nowUtc + Duration;
    }

    /// <summary>EC-019-3 re-check predicate: paused, grace set, and the grace end reached.</summary>
    /// <param name="status">Current subscription status.</param>
    /// <param name="graceEndsAtUtc">Stored grace end; null when no grace is running.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns><see langword="true"/> when the subscription is paused and its grace end has been reached; otherwise <see langword="false"/>.</returns>
    public static bool IsExpired(string status, DateTime? graceEndsAtUtc, DateTime nowUtc) =>
        status == PausedStatus && graceEndsAtUtc is not null && nowUtc >= graceEndsAtUtc.Value;

    /// <summary>TA-6.1 claim key: "billing-grace:{subscriptionId:n}" (46 chars, JobKey limit 64). RunAtUtc of the claim = the grace-end anchor.</summary>
    /// <param name="subscriptionId">The paused subscription.</param>
    /// <returns>The job-lock key <c>billing-grace:{subscriptionId}</c>.</returns>
    public static string ClaimJobKey(Guid subscriptionId) => $"billing-grace:{subscriptionId:n}";

    /// <summary>TA-5.2 envelope for billing.email_requested, kind billing_grace_ended (same payload shape as US-019-04).</summary>
    /// <param name="userId">The subscriber.</param>
    /// <param name="email">The subscriber's email address; PII (TA-9.4).</param>
    /// <param name="subscriptionId">The subscription whose grace ended.</param>
    /// <param name="nowUtc">UTC instant used as the event time.</param>
    /// <param name="correlationId">Correlation id to carry over from the triggering job.</param>
    /// <returns>An envelope of type <c>billing.email_requested</c> with kind <c>billing_grace_ended</c>.</returns>
    public static EventEnvelope BuildGraceEndedEnvelope(
        Guid userId, string email, Guid subscriptionId, DateTime nowUtc, Guid correlationId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = EventType.BillingEmailRequested,
            Version = 1,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            PartitionKey = userId.ToString("n"),
            Payload = new { userId, email, subscriptionId, kind = GraceEndedKind, step = 1 }
        };

    /// <summary>TA-5.2 envelope for plan.changed with reason grace_ended (TA-5.3, FR-019-8).</summary>
    /// <param name="userId">The user whose plan changed.</param>
    /// <param name="fromPlan">Plan code before the downgrade.</param>
    /// <param name="toPlan">Plan code after the downgrade.</param>
    /// <param name="nowUtc">UTC instant used as the event time.</param>
    /// <param name="correlationId">Correlation id to carry over from the triggering job.</param>
    /// <returns>An envelope of type <c>plan.changed</c> with reason <c>grace_ended</c>.</returns>
    public static EventEnvelope BuildPlanChangedEnvelope(
        Guid userId, string fromPlan, string toPlan, DateTime nowUtc, Guid correlationId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = EventType.PlanChanged,
            Version = 1,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            PartitionKey = userId.ToString("n"),
            Payload = new { userId, fromPlan, toPlan, reason = GraceEndedReason }
        };
}
