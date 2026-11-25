using MediatR;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-009-04 (TA-4.2#17): DELETE /api/v1/transfers/{id} — owner delete with refcount path. Reason is pinned at the
/// construction site (endpoint 17 passes "user"; US-011-02's admin force-delete will pass "admin" — one command, two reasons).
/// </summary>
public sealed record DeleteTransferCommand(Guid Id, Guid OwnerId, string Reason) : IRequest<MarkDeleteResult?>;
