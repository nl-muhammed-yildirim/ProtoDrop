namespace wa.application.UseCases.Billing;

/// <summary>Email is null when the user is deleted — the ledger is kept, nothing is sent.</summary>
/// <param name="Outcome">The outcome of the payment failed result.</param>
/// <param name="SubscriptionId">Identifier of the subscription of the payment failed result.</param>
/// <param name="UserId">Identifier of the user of the payment failed result.</param>
/// <param name="Email">The email of the payment failed result.</param>
public sealed record PaymentFailedResult(PaymentFailedOutcome Outcome, Guid SubscriptionId = default, Guid UserId = default, string? Email = null);
