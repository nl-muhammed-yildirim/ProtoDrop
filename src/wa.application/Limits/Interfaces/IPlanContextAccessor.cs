using wa.domain;

namespace wa.application.Limits;

/// <summary>Handlers read the request's plan through this port only — never AppUser.PlanId, IPlansStore or HttpContext (AC-020-4).</summary>
public interface IPlanContextAccessor
{
    /// <summary>Gets the plan context.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan context.</returns>
    Task<PlanContext> GetAsync(CancellationToken cancellationToken = default);
}
