namespace wa.application.UseCases.Billing;

/// <summary>Email is null when the user is deleted — the ledger is kept, nothing is sent.</summary>
public sealed record PaymentFailedResult(PaymentFailedOutcome Outcome, Guid SubscriptionId = default, Guid UserId = default, string? Email = null);
