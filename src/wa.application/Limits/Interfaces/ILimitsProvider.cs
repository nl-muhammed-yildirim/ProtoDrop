using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>Effective limit resolution (FR-053-2/3/6): base = the plan's seeded <see cref="Plan.LimitsJson"/>, then each existing <c>limits.&lt;plan&gt;.&lt;key&gt;</c> flag overrides only its one field. Every read goes through the caches — no direct store calls here.</summary>
public interface ILimitsProvider
{
    /// <summary>The plan code guest requests resolve to — pass this for guests, no account lookup required (FR-053-6).</summary>
    const string GuestPlanCode = Plan.DefaultCode;

    /// <summary>Returns the effective limits for <paramref name="planCode"/>. Null or blank resolves to <see cref="Plan.DefaultCode"/> (F-BIL-001). Unknown plans throw <see cref="PlanUnknownException"/> — API mapping pending the EC-018-3 decision (today 500 INTERNAL); DB down on cache miss propagates as the underlying exception — no silent fallback to hardcoded defaults (EC-053-3).</summary>
    /// <param name="planCode">Plan to resolve; null or blank means the default plan.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan's limits with flag overrides applied.</returns>
    Task<LimitsRecord> Resolve(string? planCode, CancellationToken cancellationToken = default);
}
