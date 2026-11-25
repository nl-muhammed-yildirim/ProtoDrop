namespace wa.application.UseCases.Billing;

/// <summary>One open past_due sequence: the latest step-1 anchor and the steps already claimed at that anchor.</summary>
/// <param name="SubscriptionId">The subscription in payment failure.</param>
/// <param name="UserId">The subscriber.</param>
/// <param name="Email">The subscriber's address for the reminder emails; PII (TA-9.4).</param>
/// <param name="AnchorUtc">UTC instant of the first failure; the 0/3/7-day offsets are measured from it.</param>
/// <param name="ClaimedSteps">Steps (1..3) already claimed for this anchor.</param>
public sealed record BillingSequenceRow(Guid SubscriptionId, Guid UserId, string Email, DateTime AnchorUtc, IReadOnlyCollection<int> ClaimedSteps);
