using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-07: the write result — old + new plan ids so the endpoint can audit {oldPlanId,newPlanId} (FR-011-5).</summary>
/// <param name="UserId">Identifier of the user of the set user plan result.</param>
/// <param name="OldPlanId">Identifier of the old plan of the set user plan result.</param>
/// <param name="NewPlanId">Identifier of the new plan of the set user plan result.</param>
public sealed record SetUserPlanResult(Guid UserId, Guid OldPlanId, Guid NewPlanId);
