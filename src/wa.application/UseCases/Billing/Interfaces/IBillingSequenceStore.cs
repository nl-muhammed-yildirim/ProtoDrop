namespace wa.application.UseCases.Billing;

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
