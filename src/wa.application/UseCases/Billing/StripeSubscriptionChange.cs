namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: a Stripe subscription.created/updated event reduced to what the mirror needs.</summary>
public sealed record StripeSubscriptionChange(
    string StripeEventId,
    string EventType,
    string StripeSubId,
    string? StripeCustomerId,
    string Status,
    string? PriceId,
    DateTime? CurrentPeriodEndUtc,
    Guid? MetadataUserId);
