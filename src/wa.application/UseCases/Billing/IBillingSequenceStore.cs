namespace wa.application.UseCases.Billing;

public enum PaymentFailedOutcome { Started, DuplicateEvent, DuplicateInvoice, Ineligible, SubscriptionUnresolved }

/// <summary>Email is null when the user is deleted — the ledger is kept, nothing is sent.</summary>
public sealed record PaymentFailedResult(PaymentFailedOutcome Outcome, Guid SubscriptionId = default, Guid UserId = default, string? Email = null);

public enum InvoicePaidOutcome { Cancelled, NoSequence, DuplicateEvent, Conflict }

/// <summary>One open past_due sequence: the latest step-1 anchor and the steps already claimed at that anchor.</summary>
public sealed record BillingSequenceRow(Guid SubscriptionId, Guid UserId, string Email, DateTime AnchorUtc, IReadOnlyCollection<int> ClaimedSteps);

/// <summary>US-019-04 (D-5): ledger port for the payment-failed email sequence. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingSequenceStore
{
    Task<PaymentFailedResult> StartPaymentFailedAsync(
        string eventId, string invoiceId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    Task<InvoicePaidOutcome> CancelSequenceAsync(
        string eventId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    Task<IReadOnlyList<BillingSequenceRow>> GetOpenSequencesAsync(CancellationToken ct);

    /// <summary>TA-6.1 claim: false when another instance already claimed (subscriptionId, step, anchor).</summary>
    Task<bool> TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, CancellationToken ct);
}
