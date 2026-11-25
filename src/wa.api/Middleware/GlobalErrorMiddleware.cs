using Microsoft.AspNetCore.Http;
using wa.domain.Errors;

namespace wa.api.Middleware;

/// <summary>
/// Second in the pipeline (after CorrelationMiddleware so its log context is active). Maps every failure to TA-4.1.3 Problem+JSON:
/// <see cref="BusinessException"/> → its closed-list status + code (EC-056-2, never swallowed into 500), any other exception →
/// 500 INTERNAL with the full stack in Serilog but none in the body (TA-10.5); logs carry the route template (never the raw path) and business codes (never their message) per TA-9.4. Minimal APIs answer unknown routes with an empty-body 404 —
/// rewritten here to a proper NOT_FOUND Problem so every 404/500 has the exact field set + correlationId.
/// </summary>
public sealed class GlobalErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalErrorMiddleware> _logger;

    /// <summary>Initializes a new instance of the <see cref="GlobalErrorMiddleware"/> class.</summary>
    /// <param name="next">Next middleware in the pipeline.</param>
    /// <param name="logger">Logger for failures; logs route templates and business codes, never raw paths or messages.</param>
    public GlobalErrorMiddleware(RequestDelegate next, ILogger<GlobalErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Runs the rest of the pipeline and converts any exception, and any unknown-route 404 without a body, into a TA-4.1.3 Problem+JSON response.</summary>
    /// <param name="context">The current HTTP context.</param>
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
            // TA-9.4: the message can carry a file name — it stays in the Problem body only; the log gets code + status (correlationId links them).
            _logger.LogWarning("Business error {Code} (HTTP {Status})", business.Code, business.Status);
            return;
        }
        catch (Exception unhandled) // non-business: 500 INTERNAL — stack stays in Serilog (TA-10.5), body gets the generic detail
        {
            _logger.LogError(unhandled, "Unhandled exception on {Route} — mapped to 500 INTERNAL", LogRoute.Of(context)); // TA-9.4: route template, never the raw path
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
