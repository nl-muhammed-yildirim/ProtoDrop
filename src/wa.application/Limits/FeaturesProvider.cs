using System.Reflection;
using System.Text.Json;
using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>Feature toggles per plan (US-018-05, FR-018-1, TA-3.4, TA-13.2): effective feature = plan row <see cref="Plan.FeaturesJson"/> AND the global <c>feature.*</c> gate. A gate can only switch a feature off, never grant one the plan row lacks. Every read goes through the caches (30 s TTL) — callers never see FeaturesJson or gate keys.</summary>
public interface IFeaturesProvider
{
    /// <summary>TA-13.2 global gate key prefix; the segment is the camelCase <see cref="PlanFeatures"/> property name.</summary>
    public const string GlobalGatePrefix = "feature.";

    /// <summary>Returns the effective features for <paramref name="planCode"/>. Null or blank resolves to <see cref="Plan.DefaultCode"/>. Unknown plans throw <see cref="PlanUnknownException"/>; DB failure on cache miss propagates. Each call returns a fresh instance.</summary>
    Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default);
}

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
