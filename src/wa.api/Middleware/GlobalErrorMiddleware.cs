using Microsoft.AspNetCore.Http;
using wa.domain.Errors;

namespace wa.api.Middleware;

/// <summary>
/// Second in the pipeline (after CorrelationMiddleware so its log context is active). Maps every failure to TA-4.1.3 Problem+JSON:
/// <see cref="BusinessException"/> → its closed-list status + code (EC-056-2, never swallowed into 500), any other exception →
/// 500 INTERNAL with the full stack in Serilog but none in the body (TA-10.5). Minimal APIs answer unknown routes with an empty-body 404 —
/// rewritten here to a proper NOT_FOUND Problem so every 404/500 has the exact field set + correlationId.
/// </summary>
public sealed class GlobalErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalErrorMiddleware> _logger;

    public GlobalErrorMiddleware(RequestDelegate next, ILogger<GlobalErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BusinessException business) // EC-056-2: own code keeps its TA-4.1.3 status — the table in ProblemCodes is the single source of truth
        {
            if (!context.Response.HasStarted)
                await ProblemWriter.WriteAsync(context, business.Status, business.Code, business.Message, business.Errors);
            _logger.LogWarning("Business error {Code} (HTTP {Status}): {Message}", business.Code, business.Status, business.Message);
            return;
        }
        catch (Exception unhandled) // non-business: 500 INTERNAL — stack stays in Serilog (TA-10.5), body gets the generic detail
        {
            _logger.LogError(unhandled, "Unhandled exception on {Path} — mapped to 500 INTERNAL", context.Request.Path);
            if (!context.Response.HasStarted)
                await ProblemWriter.WriteAsync(context, ProblemCodes.StatusOf(ProblemCodes.Internal), ProblemCodes.Internal, "An internal server error occurred.");
            return;
        }

        // Unknown route: minimal APIs set 404 with no body — rewrite to the exact TA-4.1.3 shape (correlationId included). Real 404s with a written body pass through untouched.
        if (!context.Response.HasStarted && context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            // Kestrel's HttpResponseStream doesn't support Position — the empty-body check only works under TestServer, so treat it as best-effort evidence.
            var bodyIsEmpty = true;
            try { bodyIsEmpty = context.Response.Body.Position == 0; } catch (NotSupportedException) { }
            if (bodyIsEmpty)
                await ProblemWriter.WriteAsync(context, ProblemCodes.StatusOf(ProblemCodes.NotFound), ProblemCodes.NotFound, "No route matches this request.");
        }
    }
}
