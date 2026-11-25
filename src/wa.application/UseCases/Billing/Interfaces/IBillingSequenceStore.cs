namespace wa.application.UseCases.Billing;

/// <summary>US-019-04 (D-5): ledger port for the payment-failed email sequence. Adapter lives in wa.infrastructure/Persistence.</summary>
public interface IBillingSequenceStore
{
    /// <summary>Starts the payment failed.</summary>
    /// <param name="eventId">Identifier of the event.</param>
    /// <param name="invoiceId">Identifier of the invoice.</param>
    /// <param name="stripeSubId">Identifier of the stripe sub.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The payment failed result.</returns>
    Task<PaymentFailedResult> StartPaymentFailedAsync(
        string eventId, string invoiceId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Cancels the sequence.</summary>
    /// <param name="eventId">Identifier of the event.</param>
    /// <param name="stripeSubId">Identifier of the stripe sub.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The invoice paid outcome.</returns>
    Task<InvoicePaidOutcome> CancelSequenceAsync(
        string eventId, string stripeSubId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Gets the open sequences.</summary>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching billing sequence row items.</returns>
    Task<IReadOnlyList<BillingSequenceRow>> GetOpenSequencesAsync(CancellationToken ct);

    /// <summary>TA-6.1 claim: false when another instance already claimed (subscriptionId, step, anchor).</summary>
    /// <param name="subscriptionId">Identifier of the subscription.</param>
    /// <param name="step">The step.</param>
    /// <param name="anchorUtc">The anchor UTC.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the try claim step condition holds; otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> TryClaimStepAsync(Guid subscriptionId, int step, DateTime anchorUtc, DateTime nowUtc, CancellationToken ct);
}
