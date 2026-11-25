using wa.domain;

namespace wa.application.Limits;

/// <summary>US-020-01 (FR-020-1): the one answer to "what plan is this request on" — code, limits and features resolved together.</summary>
/// <param name="PlanCode">Code of the effective plan.</param>
/// <param name="Limits">Effective limits for that plan, including admin flag overrides.</param>
/// <param name="Features">Effective feature toggles for that plan, after the global gates.</param>
public sealed record PlanContext(string PlanCode, LimitsRecord Limits, PlanFeatures Features);
