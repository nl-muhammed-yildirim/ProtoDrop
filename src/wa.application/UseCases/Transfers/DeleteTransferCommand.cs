using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-009-04 (TA-4.2#17): DELETE /api/v1/transfers/{id} — owner delete with refcount path. Reason is pinned at the
/// construction site (endpoint 17 passes "user"; US-011-02's admin force-delete will pass "admin" — one command, two reasons).
/// </summary>
/// <param name="Id">Id of the transfer to delete, from the route.</param>
/// <param name="OwnerId">Id of the signed-in owner; the store only deletes transfers this user owns.</param>
/// <param name="Reason">Why it is deleted: <c>user</c> for an owner delete; goes into the <c>transfer.deleted</c> event.</param>
public sealed record DeleteTransferCommand(Guid Id, Guid OwnerId, string Reason) : IRequest<MarkDeleteResult?>;
