using System.Reflection;
using System.Text.Json;
using wa.domain;

namespace wa.application.Limits;

/// <inheritdoc/>
public sealed class FeaturesProvider(PlansCache plans, FlagsCache flags) : IFeaturesProvider
{
    private static readonly (PropertyInfo Field, string GateKey)[] Gates = typeof(PlanFeatures)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(bool))
        .Select(p => (p, IFeaturesProvider.GlobalGatePrefix + JsonNamingPolicy.CamelCase.ConvertName(p.Name)))
        .ToArray();

    /// <summary>The global gate keys this resolver reads (feature.ads … feature.ssoScim), derived from <see cref="PlanFeatures"/>.</summary>
    public static IReadOnlyList<string> GlobalGateKeys { get; } = Gates.Select(g => g.GateKey).ToArray();

    public async Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default)
    {
        var code = string.IsNullOrWhiteSpace(planCode) ? Plan.DefaultCode : planCode;
        var plan = await plans.GetAsync(code, cancellationToken);
        var features = JsonSerializer.Deserialize<PlanFeatures>(plan.FeaturesJson)
            ?? throw new InvalidOperationException($"Plan '{code}' has no seeded FeaturesJson.");

        var rows = await flags.GetAllAsync(cancellationToken);
        foreach (var (field, gateKey) in Gates)
        {
            var gate = rows.FirstOrDefault(r => string.Equals(r.Key, gateKey, StringComparison.Ordinal));
            if (gate is null) continue; // no kill-switch row → plan row decides
            if (!(bool.TryParse(gate.Value, out var on) && on)) field.SetValue(features, false); // gate only switches off; garbled fails closed
        }
        return features;
    }
}
