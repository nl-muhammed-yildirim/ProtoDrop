using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-07: the write result — old + new plan ids so the endpoint can audit {oldPlanId,newPlanId} (FR-011-5).</summary>
/// <param name="UserId">Id of the user whose plan changed.</param>
/// <param name="OldPlanId">Plan id before the change.</param>
/// <param name="NewPlanId">Plan id after the change.</param>
public sealed record SetUserPlanResult(Guid UserId, Guid OldPlanId, Guid NewPlanId);
