using MediatR;
using System.Globalization;
using System.Text.Json;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 / US-044-01 (TA-4.2#23, FR-044-3): GET /api/v1/admin/flags — all seeded flag rows with their effective-value metadata.
/// Both roles read (T-011-01 policy table: GET is a read → both roles).
/// </summary>
public sealed record GetFlagsQuery : IRequest<IReadOnlyList<FlagView>>;

/// <summary>
/// US-044-01: one flag row as the admin Flags screen shows it. <paramref name="Type"/> is "int" or "bool" (<see cref="FlagValueRules"/>);
/// <paramref name="PlanDefault"/> is the raw seeded plan value for an integer limit (null otherwise or when the plan is unknown);
/// <paramref name="Overridden"/> is true when the row value differs from that plan default;
/// <paramref name="Description"/> (US-044-02) is the seeded one-line description (tooltip on the Features group), null when none.
/// </summary>
public sealed record FlagView(string Key, string Value, DateTime UpdatedAtUtc, string Type, string? PlanDefault, bool Overridden, string? Description);

/// <summary>
/// T-011-08 / US-044-01: list all flag rows ordered by Key ascending (no paging). Flag rows come from IFlagsStore;
/// plan defaults come through <see cref="PlansCache"/> (every plan read goes through the caches).
/// </summary>
public sealed class GetFlagsQueryHandler : IRequestHandler<GetFlagsQuery, IReadOnlyList<FlagView>>
{
    private readonly IFlagsStore _flags;
    private readonly PlansCache _plans;

    public GetFlagsQueryHandler(IFlagsStore flags, PlansCache plans)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
    }

    public async Task<IReadOnlyList<FlagView>> Handle(GetFlagsQuery request, CancellationToken cancellationToken)
    {
        var rows = (await _flags.GetAllAsync(cancellationToken)).OrderBy(f => f.Key, StringComparer.Ordinal).ToList();

        // Plan LimitsJson parsed once per plan per call; null = unknown plan or unparsable JSON.
        var planDocs = new Dictionary<string, JsonDocument?>(StringComparer.Ordinal);
        var result = new List<FlagView>(rows.Count);
        try
        {
            foreach (var row in rows)
            {
                var type = FlagValueRules.TypeOf(row.Key);
                string? planDefault = null;
                if (type == FlagValueRules.IntegerType
                    && FlagValueRules.TryGetLimitField(row.Key, out var planCode, out var field))
                {
                    if (!planDocs.TryGetValue(planCode, out var doc))
                    {
                        doc = await LoadAsync(planCode, cancellationToken);
                        planDocs[planCode] = doc;
                    }
                    if (doc is not null && doc.RootElement.TryGetProperty(field, out var element))
                        planDefault = element.GetRawText();
                }
                result.Add(new FlagView(row.Key, row.Value, row.UpdatedAtUtc, type, planDefault, IsOverridden(row.Value, planDefault), row.Description));
            }
        }
        finally
        {
            foreach (var doc in planDocs.Values) doc?.Dispose();
        }
        return result;
    }

    private async Task<JsonDocument?> LoadAsync(string planCode, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await _plans.GetAsync(planCode, cancellationToken);
            return JsonDocument.Parse(plan.LimitsJson);
        }
        catch (PlanUnknownException)
        {
            return null; // field is a limit but the plan has no seed row → no default to compare against
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsOverridden(string value, string? planDefault)
    {
        if (planDefault is null) return false;
        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var a)
            && long.TryParse(planDefault, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b))
            return a != b;
        return !string.Equals(value, planDefault, StringComparison.Ordinal);
    }
}
