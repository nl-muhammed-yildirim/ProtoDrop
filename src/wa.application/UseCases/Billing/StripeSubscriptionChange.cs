namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: a Stripe subscription.created/updated event reduced to what the mirror needs.</summary>
/// <param name="StripeEventId">Identifier of the stripe event of the stripe subscription change.</param>
/// <param name="EventType">The event type of the stripe subscription change.</param>
/// <param name="StripeSubId">Identifier of the stripe sub of the stripe subscription change.</param>
/// <param name="StripeCustomerId">Identifier of the stripe customer of the stripe subscription change.</param>
/// <param name="Status">The status of the stripe subscription change.</param>
/// <param name="PriceId">Identifier of the price of the stripe subscription change.</param>
/// <param name="CurrentPeriodEndUtc">The current period end UTC of the stripe subscription change.</param>
/// <param name="MetadataUserId">Identifier of the metadata user of the stripe subscription change.</param>
public sealed record StripeSubscriptionChange(
    string StripeEventId,
    string EventType,
    string StripeSubId,
    string? StripeCustomerId,
    string Status,
    string? PriceId,
    DateTime? CurrentPeriodEndUtc,
    Guid? MetadataUserId);
