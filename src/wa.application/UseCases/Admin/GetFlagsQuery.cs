using MediatR;
using System.Text.Json;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): GET /api/v1/admin/flags — list all seeded flag rows.
/// Both roles read (T-011-01 policy table: GET is a read → both roles).
/// </summary>
public sealed record GetFlagsQuery : IRequest<IReadOnlyList<FeatureFlag>>;

/// <summary>
/// T-011-08 (TA-4.2#23 / FR-011-2): list all flag rows ordered by Key ascending.
/// 52 fixed seeded rows — no paging. Reads use the EXISTING read port IFlagsStore.
/// </summary>
public sealed class GetFlagsQueryHandler : IRequestHandler<GetFlagsQuery, IReadOnlyList<FeatureFlag>>
{
    private readonly IFlagsStore _flags;

    public GetFlagsQueryHandler(IFlagsStore flags)
    {
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
    }

    public async Task<IReadOnlyList<FeatureFlag>> Handle(GetFlagsQuery request, CancellationToken cancellationToken)
    {
        // Deterministic key order — 52 fixed rows, no paging.
        return (await _flags.GetAllAsync(cancellationToken)).OrderBy(f => f.Key).ToList();
    }
}
