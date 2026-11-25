using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-047-03 (FR-047-6, TA-4.1.4): GET /api/v1/admin/erasures — the DPO erasure report, cursor-paged.
/// RevealEmail is true for SuperAdmin (the DPO); Operator gets the masked form (TA-9.4).
/// </summary>
public sealed record GetErasuresQuery(string? Cursor, int Limit, bool RevealEmail) : IRequest<GetErasuresResult>;
