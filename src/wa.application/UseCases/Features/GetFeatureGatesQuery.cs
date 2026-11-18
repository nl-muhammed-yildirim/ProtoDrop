using MediatR;
using wa.application.Limits;

namespace wa.application.UseCases.Features;

/// <summary>
/// US-044-02 (FR-044-7, AC-044-2): GET /api/v1/features — the six phase-gate booleans keyed by full gate key, in <see cref="FeatureGates.All"/> order.
/// An absent or unreadable row reads as off (EC-044-6).
/// </summary>
public sealed record GetFeatureGatesQuery : IRequest<IReadOnlyDictionary<string, bool>>;

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
