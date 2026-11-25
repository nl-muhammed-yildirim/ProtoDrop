namespace wa.application.UseCases.Billing;

/// <summary>US-019-04 (D-5): ledger port for the payment-failed email sequence. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingSequenceStore
{
    /// <summary>Records a payment failure in one transaction: dedups by Stripe event and invoice, marks the subscription past_due and returns what is needed to queue email #1.</summary>
    /// <param name="eventId">Stripe event id, used for webhook deduplication.</param>
    /// <param name="invoiceId">Stripe invoice id, used so a second failure event of the same invoice does not restart the sequence.</param>
    /// <param name="stripeSubId">Stripe subscription id the invoice belongs to.</param>
    /// <param name="nowUtc">Current UTC time; becomes the sequence anchor.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The outcome plus the subscription, user and email when email #1 should be queued.</returns>
    Task<PaymentFailedResult> StartPaymentFailedAsync(
        string eventId, string invoiceId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Handles a paid invoice: deduplicates the event and cancels the remaining reminder emails of the subscription.</summary>
    /// <param name="eventId">Stripe event id, used for webhook deduplication.</param>
    /// <param name="stripeSubId">Stripe subscription id whose sequence is cancelled.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>What happened to the sequence.</returns>
    Task<InvoicePaidOutcome> CancelSequenceAsync(
        string eventId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Lists the payment-failed sequences that are still open (subscription past_due with unsent steps).</summary>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>One row per open sequence; empty when none.</returns>
    Task<IReadOnlyList<BillingSequenceRow>> GetOpenSequencesAsync(CancellationToken ct);

    /// <summary>TA-6.1 claim: false when another instance already claimed (subscriptionId, step, anchor).</summary>
    /// <param name="subscriptionId">The subscription the step belongs to.</param>
    /// <param name="step">Step number (1..3).</param>
    /// <param name="anchorUtc">Anchor of the sequence the step belongs to.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when this caller claimed the step and must send the email; <see langword="false"/> when another instance already did.</returns>
    Task<bool> TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, CancellationToken ct);
}
