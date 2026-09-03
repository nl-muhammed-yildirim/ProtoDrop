using Serilog;
using Serilog.Context;

namespace wa.api.Pipeline;

/// <summary>
/// W3C traceparent middleware (TA-4.1.3 / US-012-01):
/// reads the incoming <c>traceparent</c> (validates + normalizes, or
/// generates a fresh one when absent/invalid), populates the request-scoped
/// <see cref="CorrelationContext"/> (correlation id = first 8 hex of the
/// trace-id, TA-10.1) which is available to the whole request pipeline,
/// and echoes the canonical <c>traceparent</c> on every response.
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string traceParent = W3CTraceParser.Parse(context.Request.Headers.TraceParent)
            ?? W3CTraceParser.Generate();

        context.RequestServices
            .GetRequiredService<CorrelationContext>()
            .Populate(traceParent);

        context.Response.Headers.TraceParent = traceParent;

        string correlationId = W3CTraceParser.CorrelationIdOf(traceParent);
        using var suspendedLogContext = LogContext.Suspend();
        LogContext.PushProperty("correlationId", correlationId);

        await _next(context);
    }
}
