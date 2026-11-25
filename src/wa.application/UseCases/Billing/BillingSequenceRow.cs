namespace wa.application.UseCases.Billing;

/// <summary>One open past_due sequence: the latest step-1 anchor and the steps already claimed at that anchor.</summary>
/// <param name="SubscriptionId">Identifier of the subscription of the billing sequence row.</param>
/// <param name="UserId">Identifier of the user of the billing sequence row.</param>
/// <param name="Email">The email of the billing sequence row.</param>
/// <param name="AnchorUtc">The anchor UTC of the billing sequence row.</param>
/// <param name="ClaimedSteps">The claimed steps of the billing sequence row.</param>
public sealed record BillingSequenceRow(Guid SubscriptionId, Guid UserId, string Email, DateTime AnchorUtc, IReadOnlyCollection<int> ClaimedSteps);
