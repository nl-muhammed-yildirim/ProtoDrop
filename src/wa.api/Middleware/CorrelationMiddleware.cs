using wa.domain.Correlation;
using Serilog.Context;

namespace wa.api.Middleware;

/// <summary>
/// First middleware in the pipeline (FR-056-1): validates or generates the W3C traceparent, stores it in
/// <see cref="CorrelationContext"/>, and pushes correlationId + traceparent into Serilog's log context so every
/// log line for this request/response pair carries them. Echoes the header back (TA-10.1 "traceparent in → out").
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var traceparent = W3CTraceParser.Parse(httpContext.Request.Headers.TraceParent.ToString());
        if (traceparent is null) // absent or malformed/wrong-version → treat as absent (EC-056-1), never crash
            traceparent = new TraceParent(W3CTraceParser.SupportedVersion, W3CTraceParser.NewTraceId(), W3CTraceParser.NewTraceId()[..16], "07");

        CorrelationContext.Set(traceparent);

        using (LogContext.PushProperty("CorrelationId", traceparent.CorrelationId))
        using (LogContext.PushProperty("TraceParent", traceparent.ToHeader()))
        {
            httpContext.Response.Headers.TraceParent = traceparent.ToHeader(); // out direction — App Insights picks it up downstream
            await _next(httpContext);
        }
    }
}
