namespace wa.application.UseCases.Billing;

/// <summary>One open past_due sequence: the latest step-1 anchor and the steps already claimed at that anchor.</summary>
public sealed record BillingSequenceRow(Guid SubscriptionId, Guid UserId, string Email, DateTime AnchorUtc, IReadOnlyCollection<int> ClaimedSteps);
