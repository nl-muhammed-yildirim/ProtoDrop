using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using wa.domain;

namespace wa.application.Limits;

/// <inheritdoc/>
public sealed class LimitsProvider(PlansCache plans, FlagsCache flags) : ILimitsProvider, IFlagResolver
{
    /// <summary>LimitsJson camelCase key → property. Derived from the POCO's [JsonPropertyName] contract (FR-053-1), so no name literals live here; segments with no match are unknown and ignored by limit resolution (EC-053-1).</summary>
    private static readonly Dictionary<string, PropertyInfo> LimitFields = BuildFieldMap();

    /// <inheritdoc />
    public async Task<LimitsRecord> Resolve(string? planCode, CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(planCode) ? Plan.DefaultCode : planCode; // F-BIL-001: Resolve(null) → default plan, never a literal.
        var plan = await plans.GetAsync(code, cancellationToken); // PlanUnknownException / DB failure propagate (AC-053-4, EC-053-3).
        var record = JsonSerializer.Deserialize<LimitsRecord>(plan.LimitsJson)
            ?? throw new InvalidOperationException($"Plan '{code}' has no seeded LimitsJson.");

        var rows = await flags.GetAllAsync(cancellationToken);
        var prefix = "limits." + code + "."; // only this plan's keys — both other plans stay unchanged (AC-053-2).
        foreach (var row in rows)
        {
            if (!row.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!LimitFields.TryGetValue(row.Key.AsSpan(prefix.Length).ToString(), out var field)) continue; // unknown segment → ignore (EC-053-1)
            field.SetValue(record, Convert.ChangeType(row.Value, field.PropertyType, CultureInfo.InvariantCulture));
        }
        return record;
    }

    /// <inheritdoc />
    public async Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        var rows = await flags.GetAllAsync(cancellationToken);
        var row = rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.Ordinal));
        if (row is null) return defaultValue; // absent = default, not error (FR-044-3)
        try { return JsonSerializer.Deserialize<T>(row.Value) is { } value ? value : defaultValue; }
        catch (JsonException) { return defaultValue; } // garbled → default (closed for gates)
        catch (NotSupportedException) { return defaultValue; }
    }

    private static Dictionary<string, PropertyInfo> BuildFieldMap()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in typeof(LimitsRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            map[property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name] = property;
        return map;
    }
}
