using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>
/// US-019-04 (D-2, FR-019-2): pure schedule + ledger-key rules for the three payment-failed emails (0 / 3 / 7 days).
/// BCL only — no clock, no I/O.
/// </summary>
public static class BillingEmailSchedule
{
    public const int StepCount = 3;
    public const string Kind = "billing_payment_failed";

    /// <summary>FR-019-2: email #1 immediately, #2 at +3 days, #3 at +7 days after the first failure.</summary>
    public static readonly IReadOnlyList<TimeSpan> Offsets = [TimeSpan.Zero, TimeSpan.FromDays(3), TimeSpan.FromDays(7)];

    /// <summary>EC-019-2: canceled / incomplete / unpaid / paused subscriptions get no sequence.</summary>
    public static bool IsEligible(string status) =>
        status is "active" or "trialing" or "past_due";

    /// <summary>The lowest unclaimed step (1..3) once its offset has elapsed; never skips ahead. Null when nothing is due.</summary>
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
    public static string StepJobKey(Guid subscriptionId, int step) =>
        $"billing-pf:{subscriptionId:n}:{step}";

    /// <summary>Per-invoice dedup key stored in StripeEvent; Stripe ids start with evt_, so "pf:" cannot collide.</summary>
    public static string InvoiceDedupKey(string invoiceId) => "pf:" + invoiceId;

    /// <summary>TA-5.2 envelope for a billing.email_requested event.</summary>
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
