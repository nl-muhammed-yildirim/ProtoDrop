using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using wa.domain;

namespace wa.application.Limits;

/// <summary>
/// US-044-01 (FR-044-5, TA-3.4, TA-13.2): the single owner of "what type is a flag key" and "is this raw value valid for it".
/// Pure and stateless — no I/O. A key is an integer limit iff it is <c>limits.&lt;plan&gt;.&lt;field&gt;</c> and the field is a
/// <see cref="LimitsRecord"/> JSON property (same derivation as <see cref="LimitsProvider"/>); every other key is boolean.
/// </summary>
public static class FlagValueRules
{
    /// <summary>Wire type name of an integer limit key.</summary>
    public const string IntegerType = "int";

    /// <summary>Wire type name of a boolean key (feature gates and boolean limit segments).</summary>
    public const string BooleanType = "bool";

    private const string LimitsPrefix = "limits.";

    /// <summary>LimitsJson camelCase field → property, derived from the POCO's [JsonPropertyName] contract.</summary>
    private static readonly Dictionary<string, PropertyInfo> LimitFields = BuildFieldMap();

    /// <summary>Fields whose Part-2 sentinel is -1 = unlimited (MaxDownloads, ActiveTransfersMax) — names derived, not literal.</summary>
    private static readonly HashSet<string> UnlimitedSentinelFields = new(StringComparer.Ordinal)
    {
        JsonNamingPolicy.CamelCase.ConvertName(nameof(LimitsRecord.MaxDownloads)),
        JsonNamingPolicy.CamelCase.ConvertName(nameof(LimitsRecord.ActiveTransfersMax)),
    };

    /// <summary>True when <paramref name="key"/> is <c>limits.&lt;plan&gt;.&lt;field&gt;</c> with a known <see cref="LimitsRecord"/> field.</summary>
    public static bool TryGetLimitField(string key, out string planCode, out string field)
    {
        planCode = string.Empty;
        field = string.Empty;
        if (key is null || !key.StartsWith(LimitsPrefix, StringComparison.Ordinal)) return false;

        var parts = key.Split('.');
        if (parts.Length != 3 || parts[1].Length == 0 || !LimitFields.ContainsKey(parts[2])) return false;

        planCode = parts[1];
        field = parts[2];
        return true;
    }

    /// <summary>The wire type of a key: <see cref="IntegerType"/> for a known limit field, otherwise <see cref="BooleanType"/>.</summary>
    public static string TypeOf(string key) =>
        TryGetLimitField(key, out _, out _) ? IntegerType : BooleanType;

    /// <summary>True when <paramref name="value"/> is a valid raw value for <paramref name="key"/> (EC-044-1, EC-044-5).</summary>
    public static bool IsValid(string key, string value)
    {
        if (value is null) return false;

        if (!TryGetLimitField(key, out _, out var field))
            return value is "true" or "false";

        // Integer limit: plain signed integer text only (no exponent, fraction, quotes or whitespace).
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)) return false;

        var type = LimitFields[field].PropertyType;
        if (type == typeof(int) && (number < int.MinValue || number > int.MaxValue)) return false; // Convert.ChangeType at resolve time must never throw.

        return number > 0 || (number == -1 && UnlimitedSentinelFields.Contains(field));
    }

    private static Dictionary<string, PropertyInfo> BuildFieldMap()
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in typeof(LimitsRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            map[property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name] = property;
        return map;
    }
}
