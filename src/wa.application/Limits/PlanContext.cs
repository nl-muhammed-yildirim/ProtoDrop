using wa.domain;

namespace wa.application.Limits;

/// <summary>US-020-01 (FR-020-1): the one answer to "what plan is this request on" — code, limits and features resolved together.</summary>
/// <param name="PlanCode">The plan code of the plan context.</param>
/// <param name="Limits">The limits of the plan context.</param>
/// <param name="Features">The features of the plan context.</param>
public sealed record PlanContext(string PlanCode, LimitsRecord Limits, PlanFeatures Features);
