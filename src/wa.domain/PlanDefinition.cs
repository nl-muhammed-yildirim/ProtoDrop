using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace wa.domain;

/// <summary>Validates a <see cref="Plan"/> row before it is written (EC-018-2, TA-3.2/3.4): plans are data, so a malformed LimitsJson/FeaturesJson/PriceJson must never reach the table. Writers call this first (validate-then-write); an empty list means valid.</summary>
public static class PlanDefinition
{
    private const int CodeMaxLength = 32; // VARCHAR(32), TA-3.2
    private const int NameMaxLength = 64; // VARCHAR(64), TA-3.2

    // Key sets are derived from the POCO [JsonPropertyName] contract — same source LimitsProvider uses — so no key literals are duplicated.
    private static readonly IReadOnlyList<PropertyInfo> LimitFields = Fields(typeof(LimitsRecord));
    private static readonly IReadOnlyList<PropertyInfo> FeatureFields = Fields(typeof(PlanFeatures));
    private static readonly IReadOnlyList<PropertyInfo> PriceFields = Fields(typeof(PlanPrice));
    private static readonly IReadOnlyList<PropertyInfo> TierFields = Fields(typeof(PlanPriceTier));

    private const string StripePricePrefix = "price_"; // Stripe Price object IDs; catches a pasted prod_ product ID.
    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.CultureInvariant); // ISO 4217 shape only; no currency literal.

    // Part-2 "unlimited" sentinel (-1) is allowed on these two only.
    private static readonly HashSet<string> UnlimitedSentinelProperties = [nameof(LimitsRecord.MaxDownloads), nameof(LimitsRecord.ActiveTransfersMax)];

    /// <summary>Returns every problem found (human-readable, naming the field); empty = valid.</summary>
    /// <param name="plan">The plan row about to be written.</param>
    /// <returns>One human-readable message per problem found, naming the offending field; empty when the plan is valid.</returns>
    public static IReadOnlyList<string> Validate(Plan plan)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(plan.Code)) errors.Add("Code must not be empty.");
        else
        {
            if (plan.Code.Length > CodeMaxLength) errors.Add($"Code must be at most {CodeMaxLength} characters.");
            if (plan.Code.Contains('.')) errors.Add("Code must not contain '.' (it is a segment of the limits flag key).");
        }

        if (string.IsNullOrWhiteSpace(plan.Name)) errors.Add("Name must not be empty.");
        else if (plan.Name.Length > NameMaxLength) errors.Add($"Name must be at most {NameMaxLength} characters.");

        ValidateLimits(plan.LimitsJson, errors);
        ValidateFeatures(plan.FeaturesJson, errors);
        ValidatePrice(plan.PriceJson, errors);
        return errors;
    }

    private static void ValidatePrice(string? json, List<string> errors)
    {
        if (!TryParseObject(json, "PriceJson", errors, out var doc)) return;
        using (doc)
        {
            var root = doc!.RootElement;
            CheckKeySet(root, PriceFields, "PriceJson", errors);

            if (root.TryGetProperty(KeyOf(PriceProperty(nameof(PlanPrice.Currency))), out var currency)
                && (currency.ValueKind != JsonValueKind.String || !CurrencyPattern.IsMatch(currency.GetString()!)))
                errors.Add($"PriceJson.{KeyOf(PriceProperty(nameof(PlanPrice.Currency)))} must be a 3-letter uppercase ISO 4217 code.");

            foreach (var name in new[] { nameof(PlanPrice.Monthly), nameof(PlanPrice.Annual) })
            {
                var key = KeyOf(PriceProperty(name));
                if (!root.TryGetProperty(key, out var tier)) continue;
                ValidateTier(tier, $"PriceJson.{key}", errors);
            }

            var perSeatKey = KeyOf(PriceProperty(nameof(PlanPrice.PerSeatMonthly)));
            if (root.TryGetProperty(perSeatKey, out var perSeat) && perSeat.ValueKind != JsonValueKind.Null)
                CheckAmount(perSeat, $"PriceJson.{perSeatKey}", errors);
        }
    }

    private static void ValidateTier(JsonElement tier, string path, List<string> errors)
    {
        if (tier.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path} must be a JSON object.");
            return;
        }
        CheckKeySet(tier, TierFields, path, errors);

        var amountKey = KeyOf(typeof(PlanPriceTier).GetProperty(nameof(PlanPriceTier.Amount))!);
        if (tier.TryGetProperty(amountKey, out var amount)) CheckAmount(amount, $"{path}.{amountKey}", errors);

        var idKey = KeyOf(typeof(PlanPriceTier).GetProperty(nameof(PlanPriceTier.StripePriceId))!);
        if (tier.TryGetProperty(idKey, out var id) && id.ValueKind != JsonValueKind.Null)
        {
            var value = id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(StripePricePrefix, StringComparison.Ordinal))
                errors.Add($"{path}.{idKey} must be null or a Stripe price ID starting with '{StripePricePrefix}'.");
        }
    }

    private static void CheckAmount(JsonElement value, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Number) { errors.Add($"{path} must be a number."); return; }
        if (!value.TryGetInt64(out var number)) { errors.Add($"{path} must be a whole number that fits Int64."); return; }
        if (number < 0) errors.Add($"{path} must be >= 0.");
    }

    private static PropertyInfo PriceProperty(string name) => typeof(PlanPrice).GetProperty(name)!;

    private static void ValidateLimits(string? json, List<string> errors)
    {
        if (!TryParseObject(json, "LimitsJson", errors, out var doc)) return;
        using (doc)
        {
            var root = doc!.RootElement;
            CheckKeySet(root, LimitFields, "LimitsJson", errors);
            foreach (var field in LimitFields)
            {
                var key = KeyOf(field);
                if (!root.TryGetProperty(key, out var value)) continue;
                if (value.ValueKind != JsonValueKind.Number)
                {
                    errors.Add($"LimitsJson.{key} must be a number.");
                    continue;
                }
                long number;
                var fits = field.PropertyType == typeof(long)
                    ? value.TryGetInt64(out number)
                    : TryGetInt32AsLong(value, out number);
                if (!fits)
                {
                    errors.Add($"LimitsJson.{key} must be a whole number that fits {field.PropertyType.Name}.");
                    continue;
                }
                var min = UnlimitedSentinelProperties.Contains(field.Name) ? -1 : 0;
                if (number < min) errors.Add($"LimitsJson.{key} must be >= {min}.");
            }
        }
    }

    private static void ValidateFeatures(string? json, List<string> errors)
    {
        if (!TryParseObject(json, "FeaturesJson", errors, out var doc)) return;
        using (doc)
        {
            var root = doc!.RootElement;
            CheckKeySet(root, FeatureFields, "FeaturesJson", errors);
            foreach (var field in FeatureFields)
            {
                var key = KeyOf(field);
                if (root.TryGetProperty(key, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    errors.Add($"FeaturesJson.{key} must be true or false.");
            }
        }
    }

    private static bool TryGetInt32AsLong(JsonElement value, out long number)
    {
        var ok = value.TryGetInt32(out var i);
        number = i;
        return ok;
    }

    private static bool TryParseObject(string? json, string name, List<string> errors, out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(json)) { errors.Add($"{name} must not be empty."); return false; }
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { errors.Add($"{name} is not valid JSON."); return false; }
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            doc = null;
            errors.Add($"{name} must be a JSON object.");
            return false;
        }
        return true;
    }

    private static void CheckKeySet(JsonElement root, IReadOnlyList<PropertyInfo> expected, string name, List<string> errors)
    {
        var expectedKeys = expected.Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        foreach (var key in expectedKeys)
            if (!root.TryGetProperty(key, out _)) errors.Add($"{name}.{key} is missing.");
        foreach (var actual in root.EnumerateObject())
            if (!expectedKeys.Contains(actual.Name)) errors.Add($"{name}.{actual.Name} is not a known key.");
    }

    private static IReadOnlyList<PropertyInfo> Fields(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static string KeyOf(PropertyInfo property) => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
}
