using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): DELETE /api/v1/admin/users/{id} — SuperAdmin-only force-delete.
/// Reuses T-008's GDPR soft-delete + re-home verbatim (reason "admin" is the admin context, not a column).
/// </summary>
/// <param name="UserId">Id of the user to delete, from the route.</param>
public sealed record AdminForceDeleteUserCommand(Guid UserId) : IRequest<AdminForceDeleteResult?>;
