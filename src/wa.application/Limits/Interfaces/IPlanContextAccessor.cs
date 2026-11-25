using wa.domain;

namespace wa.application.Limits;

/// <summary>Handlers read the request's plan through this port only — never AppUser.PlanId, IPlansStore or HttpContext (AC-020-4).</summary>
public interface IPlanContextAccessor
{
    Task<PlanContext> GetAsync(CancellationToken cancellationToken = default);
}
