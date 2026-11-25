using MediatR;
using System.Text.Json;
using wa.application.Limits;
using wa.application.Ports;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 / US-044-01 (TA-4.2#23, FR-044-3): GET /api/v1/admin/flags — all seeded flag rows with their effective-value metadata.
/// Both roles read (T-011-01 policy table: GET is a read → both roles).
/// </summary>
public sealed record GetFlagsQuery : IRequest<IReadOnlyList<FlagView>>;
