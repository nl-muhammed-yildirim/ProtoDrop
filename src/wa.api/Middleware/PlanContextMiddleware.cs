using Microsoft.AspNetCore.Http;
using wa.application.Limits;

namespace wa.api.Middleware;

/// <summary>
/// US-020-01 (FR-020-1): binds the session user (null = guest) into the scoped PlanContextAccessor.
/// No DB call and no response writes — resolution happens on the first IPlanContextAccessor.GetAsync in the request.
/// </summary>
/// <param name="next">Next middleware in the pipeline.</param>
public sealed class PlanContextMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    /// <summary>Binds the session user, or null for a guest, to the request-scoped plan accessor and continues the pipeline.</summary>
    /// <param name="context">The current HTTP context; the session middleware has already stored the user id in its items.</param>
    /// <param name="accessor">The request-scoped accessor that resolves the plan lazily.</param>
    public async Task InvokeAsync(HttpContext context, PlanContextAccessor accessor)
    {
        accessor.Bind(context.Items[SessionMiddleware.SessionUserIdKey] as Guid?);
        await _next(context);
    }
}
