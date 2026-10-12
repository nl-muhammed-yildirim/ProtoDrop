using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>Effective limit resolution (FR-053-2/3/6): base = the plan's seeded <see cref="Plan.LimitsJson"/>, then each existing <c>limits.&lt;plan&gt;.&lt;key&gt;</c> flag overrides only its one field. Every read goes through the caches — no direct store calls here.</summary>
public interface ILimitsProvider
{
    /// <summary>The plan code guest requests resolve to — pass this for guests, no account lookup required (FR-053-6).</summary>
    public const string GuestPlanCode = "free";

    /// <summary>Returns the effective limits for <paramref name="planCode"/>. Unknown plans throw <see cref="PlanUnknownException"/> → 404 PLAN_UNKNOWN at the API layer (AC-053-4); DB down on cache miss propagates as the underlying exception — no silent fallback to hardcoded defaults (EC-053-3).</summary>
    Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default);
}

/// <inheritdoc/>
public sealed class LimitsProvider(PlansCache plans, FlagsCache flags) : ILimitsProvider
{
    /// <summary>LimitsJson camelCase key → property. Derived from the POCO's [JsonPropertyName] contract (FR-053-1), so no name literals live here; segments with no match are unknown and ignored by limit resolution (EC-053-1).</summary>
    private static readonly Dictionary<string, PropertyInfo> LimitFields = BuildFieldMap();

    public async Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken)
    {
        var plan = await plans.GetAsync(planCode, cancellationToken); // PlanUnknownException / DB failure propagate (AC-053-4, EC-053-3).
        var record = JsonSerializer.Deserialize<LimitsRecord>(plan.LimitsJson)
            ?? throw new InvalidOperationException($"Plan '{planCode}' has no seeded LimitsJson.");

        var rows = await flags.GetAllAsync(cancellationToken);
        var prefix = "limits." + planCode + "."; // only this plan's keys — both other plans stay unchanged (AC-053-2).
        foreach (var row in rows)
        {
            if (!row.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!LimitFields.TryGetValue(row.Key.AsSpan(prefix.Length).ToString(), out var field)) continue; // unknown segment → ignore (EC-053-1)
            field.SetValue(record, Convert.ChangeType(row.Value, field.PropertyType, CultureInfo.InvariantCulture));
        }
        return record;
    }

    private static Dictionary<string, PropertyInfo> BuildFieldMap()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in typeof(LimitsRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            map[property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name] = property;
        return map;
    }
}
