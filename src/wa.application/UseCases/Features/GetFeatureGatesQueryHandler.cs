using MediatR;
using wa.application.Limits;

namespace wa.application.UseCases.Features;

/// <summary>Handles <see cref="GetFeatureGatesQuery"/>: resolves every phase-gate flag and returns its on/off state keyed by flag key.</summary>
/// <param name="flags">Resolver that reads the flag values.</param>
public sealed class GetFeatureGatesQueryHandler(IFlagResolver flags) : IRequestHandler<GetFeatureGatesQuery, IReadOnlyDictionary<string, bool>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, bool>> Handle(GetFeatureGatesQuery request, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool>(FeatureGates.All.Count, StringComparer.Ordinal);
        foreach (var key in FeatureGates.All)
            result[key] = await flags.ResolveFlag(key, FeatureGates.DefaultWhenAbsent, cancellationToken);
        return result;
    }
}
