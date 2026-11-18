using Microsoft.AspNetCore.Http;
using wa.application.Limits;

namespace wa.api.Middleware;

/// <summary>
/// US-020-01 (FR-020-1): binds the session user (null = guest) into the scoped PlanContextAccessor.
/// No DB call and no response writes — resolution happens on the first IPlanContextAccessor.GetAsync in the request.
/// </summary>
public sealed class PlanContextMiddleware
{
    private readonly RequestDelegate _next;

    public PlanContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, PlanContextAccessor accessor)
    {
        accessor.Bind(context.Items[SessionMiddleware.SessionUserIdKey] as Guid?);
        await _next(context);
    }
}
