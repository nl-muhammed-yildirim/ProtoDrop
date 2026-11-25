using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Transfers;

/// <summary>F-PRF-004 (FR-024-3): whether the recipient page may render the ad slot — global feature.ads gate AND the sender's CURRENT plan (EC-024-6).</summary>
public static class RecipientAds
{
    /// <summary>Decides whether the recipient page shows its ad slot: the global ads gate must be on and the sender's current plan must include ads.</summary>
    /// <param name="flags">Resolver for the ads feature gate.</param>
    /// <param name="users">Store used to resolve the sender's plan code.</param>
    /// <param name="features">Provider of the plan's feature toggles.</param>
    /// <param name="ownerAppUserId">The sending user; null for anonymous senders, who count as the default plan.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the ad slot may be rendered; otherwise <see langword="false"/>.</returns>
    public static async Task<bool> ResolveAsync(
        IFlagResolver flags, IUserPlanStore users, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct)
    {
        // Kill-switch first, fail closed when absent/garbled (FR-044-3 / EC-044-6) — and no plan lookup while ads are off (launch default, D-14).
        if (!await flags.ResolveFlag(FeatureGates.Ads, FeatureGates.DefaultWhenAbsent, ct))
            return false;

        // Guest sender or missing user row → the guest/default plan (PlanContextAccessor precedent).
        var planCode = ownerAppUserId is { } id
            ? await users.GetPlanCodeAsync(id, ct) ?? ILimitsProvider.GuestPlanCode
            : ILimitsProvider.GuestPlanCode;

        return (await features.Resolve(planCode, ct)).Ads; // Pro/Business rows are false → no slot (AC-024-2)
    }
}
