namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: a Stripe subscription.created/updated event reduced to what the mirror needs.</summary>
/// <param name="StripeEventId">Stripe event id, used for webhook deduplication.</param>
/// <param name="EventType">Stripe event type, for example <c>customer.subscription.updated</c>.</param>
/// <param name="StripeSubId">Stripe subscription id.</param>
/// <param name="StripeCustomerId">Stripe customer id; null when absent.</param>
/// <param name="Status">Stripe subscription status, for example <c>active</c> or <c>paused</c>.</param>
/// <param name="PriceId">Stripe Price id of the first subscription item; null when absent.</param>
/// <param name="CurrentPeriodEndUtc">End of the current paid period; null when Stripe does not report one.</param>
/// <param name="MetadataUserId">User id stored in the subscription metadata at checkout; null when absent.</param>
public sealed record StripeSubscriptionChange(
    string StripeEventId,
    string EventType,
    string StripeSubId,
    string? StripeCustomerId,
    string Status,
    string? PriceId,
    DateTime? CurrentPeriodEndUtc,
    Guid? MetadataUserId);
