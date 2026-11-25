using System.Text.Json;
using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>Feature toggles per plan (US-018-05, FR-018-1, TA-3.4, TA-13.2): effective feature = plan row <see cref="Plan.FeaturesJson"/> AND the global <c>feature.*</c> gate. A gate can only switch a feature off, never grant one the plan row lacks. Every read goes through the caches (30 s TTL) — callers never see FeaturesJson or gate keys.</summary>
public interface IFeaturesProvider
{
    /// <summary>TA-13.2 global gate key prefix; the segment is the camelCase <see cref="PlanFeatures"/> property name.</summary>
    public const string GlobalGatePrefix = "feature.";

    /// <summary>Returns the effective features for <paramref name="planCode"/>. Null or blank resolves to <see cref="Plan.DefaultCode"/>. Unknown plans throw <see cref="PlanUnknownException"/>; DB failure on cache miss propagates. Each call returns a fresh instance.</summary>
    /// <param name="planCode">The plan code.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan features.</returns>
    Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default);
}
