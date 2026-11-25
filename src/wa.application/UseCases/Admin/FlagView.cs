using wa.application.Limits;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-044-01: one flag row as the admin Flags screen shows it. <paramref name="Type"/> is "int" or "bool" (<see cref="FlagValueRules"/>);
/// <paramref name="PlanDefault"/> is the raw seeded plan value for an integer limit (null otherwise or when the plan is unknown);
/// <paramref name="Overridden"/> is true when the row value differs from that plan default;
/// <paramref name="Description"/> (US-044-02) is the seeded one-line description (tooltip on the Features group), null when none.
/// </summary>
/// <param name="Key">Flag key, for example a <c>limits.{plan}.{field}</c> override or a <c>feature.*</c> gate.</param>
/// <param name="Value">Raw stored value as text.</param>
/// <param name="UpdatedAtUtc">UTC instant the value last changed.</param>
/// <param name="Type">Wire type of the key, <c>int</c> or <c>bool</c>.</param>
/// <param name="PlanDefault">Seeded plan value for an integer limit; null otherwise.</param>
/// <param name="Overridden">True when <paramref name="Value"/> differs from <paramref name="PlanDefault"/>.</param>
/// <param name="Description">Seeded one-line description shown as a tooltip; null when none.</param>
public sealed record FlagView(string Key, string Value, DateTime UpdatedAtUtc, string Type, string? PlanDefault, bool Overridden, string? Description);
