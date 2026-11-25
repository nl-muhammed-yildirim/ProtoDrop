using MediatR;
using wa.application.Limits;

namespace wa.application.UseCases.Features;

public sealed class GetFeatureGatesQueryHandler(IFlagResolver flags) : IRequestHandler<GetFeatureGatesQuery, IReadOnlyDictionary<string, bool>>
{
    public async Task<IReadOnlyDictionary<string, bool>> Handle(GetFeatureGatesQuery request, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, bool>(FeatureGates.All.Count, StringComparer.Ordinal);
        foreach (var key in FeatureGates.All)
            result[key] = await flags.ResolveFlag(key, FeatureGates.DefaultWhenAbsent, cancellationToken);
        return result;
    }
}
