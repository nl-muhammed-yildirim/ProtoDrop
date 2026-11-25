namespace wa.application.Limits;

/// <summary>Handlers read the request's plan through this port only — never AppUser.PlanId, IPlansStore or HttpContext (AC-020-4).</summary>
public interface IPlanContextAccessor
{
    /// <summary>Returns the plan context of the current request, resolving it on first use.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan code, limits and features of the session user, or of the default plan for guests.</returns>
    Task<PlanContext> GetAsync(CancellationToken cancellationToken = default);
}
