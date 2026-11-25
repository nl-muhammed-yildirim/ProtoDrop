namespace wa.application.UseCases.Billing;

/// <summary>Result of starting a payment-failed sequence. The email is null when the user is deleted: the ledger is kept but nothing is sent.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="SubscriptionId">The subscription in payment failure; default unless started.</param>
/// <param name="UserId">The subscriber; default unless started.</param>
/// <param name="Email">Address for email #1; PII (TA-9.4).</param>
public sealed record PaymentFailedResult(PaymentFailedOutcome Outcome, Guid SubscriptionId = default, Guid UserId = default, string? Email = null);
