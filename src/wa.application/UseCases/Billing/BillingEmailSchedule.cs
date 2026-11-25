using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-04 (D-2, FR-019-2): pure schedule + ledger-key rules for the three payment-failed emails (0 / 3 / 7 days).
/// BCL only — no clock, no I/O.
/// </summary>
public static class BillingEmailSchedule
{
    /// <summary>Number of payment-failed emails in the sequence (day 0, day 3 and day 7).</summary>
    public const int StepCount = 3;
    /// <summary>Email kind string for the payment-failed sequence (<c>billing_payment_failed</c>).</summary>
    public const string Kind = "billing_payment_failed";

    /// <summary>FR-019-2: email #1 immediately, #2 at +3 days, #3 at +7 days after the first failure.</summary>
    public static readonly IReadOnlyList<TimeSpan> Offsets = [TimeSpan.Zero, TimeSpan.FromDays(3), TimeSpan.FromDays(7)];

    /// <summary>EC-019-2: canceled / incomplete / unpaid / paused subscriptions get no sequence.</summary>
    /// <param name="status">Stripe subscription status.</param>
    /// <returns><see langword="true"/> for <c>active</c>, <c>trialing</c> and <c>past_due</c> subscriptions; otherwise <see langword="false"/>.</returns>
    public static bool IsEligible(string status) =>
        status is "active" or "trialing" or "past_due";

    /// <summary>The lowest unclaimed step (1..3) once its offset has elapsed; never skips ahead. Null when nothing is due.</summary>
    /// <param name="anchorUtc">UTC instant of the first payment failure; the offsets are measured from it.</param>
    /// <param name="claimedSteps">Steps (1..3) that were already sent or claimed.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns>The step number to send now, or <see langword="null"/> when no further step is due.</returns>
    public static int? NextDueStep(DateTime anchorUtc, IReadOnlyCollection<int> claimedSteps, DateTime nowUtc)
    {
        for (var step = 1; step <= StepCount; step++)
        {
            if (claimedSteps.Contains(step))
                continue;
            return nowUtc >= anchorUtc + Offsets[step - 1] ? step : null;
        }
        return null;
    }

    /// <summary>TA-6.1 claim key: "billing-pf:{subscriptionId:n}:{step}" (45 chars, JobKey limit 64).</summary>
    /// <param name="subscriptionId">The subscription the failure belongs to.</param>
    /// <param name="step">Step number (1..3).</param>
    /// <returns>The job-lock key <c>billing-pf:{subscriptionId}:{step}</c>.</returns>
    public static string StepJobKey(Guid subscriptionId, int step) =>
        $"billing-pf:{subscriptionId:n}:{step}";

    /// <summary>Per-invoice dedup key stored in StripeEvent; Stripe ids start with evt_, so "pf:" cannot collide.</summary>
    /// <param name="invoiceId">Stripe invoice id.</param>
    /// <returns>The key <c>pf:{invoiceId}</c>.</returns>
    public static string InvoiceDedupKey(string invoiceId) => "pf:" + invoiceId;

    /// <summary>TA-5.2 envelope for a billing.email_requested event.</summary>
    /// <param name="userId">The subscriber.</param>
    /// <param name="email">The subscriber's email address; PII (TA-9.4).</param>
    /// <param name="subscriptionId">The subscription whose payment failed.</param>
    /// <param name="step">Step number (1..3) of the sequence.</param>
    /// <param name="nowUtc">UTC instant used as the event time.</param>
    /// <param name="correlationId">Correlation id to carry over from the triggering request or job.</param>
    /// <returns>An envelope of type <c>billing.email_requested</c> ready to publish.</returns>
    public static EventEnvelope BuildEnvelope(
        Guid userId, string email, Guid subscriptionId, int step, DateTime nowUtc, Guid correlationId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            Type = EventType.BillingEmailRequested,
            Version = 1,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            PartitionKey = userId.ToString("n"),
            Payload = new { userId, email, subscriptionId, kind = Kind, step }
        };
}
