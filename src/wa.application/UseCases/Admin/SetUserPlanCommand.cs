using MediatR;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): PATCH /api/v1/admin/users/{id}?planId= — SuperAdmin-only plan write.
/// Limits take effect at the NEXT finalize (FR-BIL-001-4 "applies to new transfers") — no limit recompute here.
/// </summary>
public sealed record SetUserPlanCommand(Guid UserId, Guid PlanId) : IRequest<SetUserPlanResult>;
