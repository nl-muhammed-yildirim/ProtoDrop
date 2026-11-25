using MediatR;
using wa.application.Limits;

namespace wa.application.UseCases.Features;

/// <summary>
/// Handles <see cref="GetFeatureGatesQuery"/> and produces <c>IReadOnlyDictionary&lt;string, bool&gt;</c>.
/// </summary>
/// <param name="flags">The flags of the get feature gates query handler.</param>
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
