using MediatR;
using wa.application.Limits;

namespace wa.application.UseCases.Features;

/// <summary>
/// US-044-02 (FR-044-7, AC-044-2): GET /api/v1/features — the six phase-gate booleans keyed by full gate key, in <see cref="FeatureGates.All"/> order.
/// An absent or unreadable row reads as off (EC-044-6).
/// </summary>
public sealed record GetFeatureGatesQuery : IRequest<IReadOnlyDictionary<string, bool>>;
