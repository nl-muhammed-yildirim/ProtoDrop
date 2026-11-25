using System.Globalization;
using System.Text.Json;
using wa.application.Limits;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-044-01: one flag row as the admin Flags screen shows it. <paramref name="Type"/> is "int" or "bool" (<see cref="FlagValueRules"/>);
/// <paramref name="PlanDefault"/> is the raw seeded plan value for an integer limit (null otherwise or when the plan is unknown);
/// <paramref name="Overridden"/> is true when the row value differs from that plan default;
/// <paramref name="Description"/> (US-044-02) is the seeded one-line description (tooltip on the Features group), null when none.
/// </summary>
/// <param name="Key">The key of the flag view.</param>
/// <param name="Value">The value of the flag view.</param>
/// <param name="UpdatedAtUtc">The updated at UTC of the flag view.</param>
/// <param name="Type">The type of the flag view.</param>
/// <param name="PlanDefault">The plan default of the flag view.</param>
/// <param name="Overridden">Whether overridden of the flag view.</param>
/// <param name="Description">The description of the flag view.</param>
public sealed record FlagView(string Key, string Value, DateTime UpdatedAtUtc, string Type, string? PlanDefault, bool Overridden, string? Description);
