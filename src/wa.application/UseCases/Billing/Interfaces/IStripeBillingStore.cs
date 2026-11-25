using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-03 (TA-3.2 Subscription + StripeEvent): the webhook handler's DB surface.</summary>
public interface IStripeBillingStore
{
    /// <summary>The plan whose PriceJson monthly or annual StripePriceId equals <paramref name="priceId"/>; null when none.</summary>
    /// <param name="priceId">Stripe Price object id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The matching plan, or <see langword="null"/> when no plan price is wired to that id.</returns>
    Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct);

    /// <summary>
    /// ONE transaction: insert StripeEvent (PK collision → Duplicate), resolve the user, upsert Subscription,
    /// set AppUser.PlanId when <paramref name="targetPlanId"/> has a value. UserUnresolved rolls everything back.
    /// </summary>
    /// <param name="change">The subscription change reduced from the webhook event.</param>
    /// <param name="targetPlanId">Plan the user moves to; null to leave the user's plan unchanged.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The outcome with the user and plan ids and codes needed for the plan-changed event.</returns>
    Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct);

    /// <summary>US-019-02: the Stripe customer id of the user's most recently updated Subscription mirror row; null when none.</summary>
    /// <param name="userId">The user whose customer id is wanted.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The Stripe customer id, or <see langword="null"/> when the user has no subscription row.</returns>
    Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct);
}
