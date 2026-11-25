using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-03 (TA-3.2 Subscription + StripeEvent): the webhook handler's DB surface.</summary>
public interface IStripeBillingStore
{
    /// <summary>The plan whose PriceJson monthly or annual StripePriceId equals <paramref name="priceId"/>; null when none.</summary>
    /// <param name="priceId">Identifier of the price.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The plan, or <see langword="null"/> when there is none.</returns>
    Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct);

    /// <summary>
    /// ONE transaction: insert StripeEvent (PK collision → Duplicate), resolve the user, upsert Subscription,
    /// set AppUser.PlanId when <paramref name="targetPlanId"/> has a value. UserUnresolved rolls everything back.
    /// </summary>
    /// <param name="change">The change.</param>
    /// <param name="targetPlanId">Identifier of the target plan.</param>
    /// <param name="nowUtc">The now UTC.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The stripe apply result.</returns>
    Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct);

    /// <summary>US-019-02: the Stripe customer id of the user's most recently updated Subscription mirror row; null when none.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The resulting text.</returns>
    Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct);
}
