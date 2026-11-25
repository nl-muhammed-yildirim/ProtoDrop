using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// US-047-03 (FR-047-6, TA-4.1.4): GET /api/v1/admin/erasures — the DPO erasure report, cursor-paged.
/// RevealEmail is true for SuperAdmin (the DPO); Operator gets the masked form (TA-9.4).
/// </summary>
/// <param name="Cursor">Opaque cursor from the previous page; null or empty for the first page.</param>
/// <param name="Limit">Maximum number of rows to return.</param>
/// <param name="RevealEmail">True to return full emails (SuperAdmin); false to mask them (Operator).</param>
public sealed record GetErasuresQuery(string? Cursor, int Limit, bool RevealEmail) : IRequest<GetErasuresResult>;
