using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-03 (TA-3.2 Subscription + StripeEvent): the webhook handler's DB surface.</summary>
public interface IStripeBillingStore
{
    /// <summary>The plan whose PriceJson monthly or annual StripePriceId equals <paramref name="priceId"/>; null when none.</summary>
    Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct);

    /// <summary>
    /// ONE transaction: insert StripeEvent (PK collision → Duplicate), resolve the user, upsert Subscription,
    /// set AppUser.PlanId when <paramref name="targetPlanId"/> has a value. UserUnresolved rolls everything back.
    /// </summary>
    Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct);

    /// <summary>US-019-02: the Stripe customer id of the user's most recently updated Subscription mirror row; null when none.</summary>
    Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct);
}
