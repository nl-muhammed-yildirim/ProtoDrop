using MediatR;
using wa.application.UseCases.Transfers;
using wa.domain.Correlation;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-05 (TA-4.2#21 / FR-011-4): DELETE /api/v1/admin/transfers/{id} — SuperAdmin-only force-delete.
/// Reason is pinned to "admin" at the construction site (endpoint 21).
/// </summary>
public sealed record AdminDeleteTransferCommand(Guid Id) : IRequest<MarkDeleteResult?>;
