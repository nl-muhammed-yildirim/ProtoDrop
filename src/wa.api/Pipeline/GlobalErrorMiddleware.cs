using Serilog;

namespace wa.api.Pipeline;

/// <summary>
/// Global error mapping (TA-4.1.3, F-TRF-013): every unhandled error becomes
/// an <c>application/problem+json</c> response —
/// <see cref="ProblemDetailsException"/> carries its own status/closed-list
/// code, and everything else is a logged 500 <c>INTERNAL</c> with only the
/// correlation id — no message, no stack trace, no exception data leaks.
/// </summary>
public sealed class GlobalErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiBaseUrl;

    public GlobalErrorMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiBaseUrl = configuration["Wa:Url:Api"] ?? string.Empty;
    }

    /// <summary>
    /// Catches all unhandled errors from the downstream pipeline and maps
    /// them to a Problem+JSON response (TA-4.1.3 wire shape, closed <c>code</c>
    /// list, no message/stack-trace leakage).
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Response already committed; hand back to host-level handling.
                throw;
            }

            if (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            string correlationId = context.RequestServices
                .GetRequiredService<CorrelationContext>().CorrelationId;

            ProblemDetailsException? problem = ex as ProblemDetailsException;

            int status;
            string type;
            string? details = null;
            IReadOnlyList<FieldError>? errors = null;

            if (problem is null)
            {
                // Unknown exception → generic 500 (TA-4.1.3 closed code list).
                status = 500;
                type = ProblemWriter.BuildTypeUri(_apiBaseUrl, ErrorCode.INTERNAL);
                Log.Warning(ex, "Unhandled exception → 500 {Code}", "INTERNAL");
            }
            else
            {
                status = problem.Status;
                type = problem.Type;
                details = problem.Details;
                errors = problem.Errors;
            }

            byte[] body = ProblemWriter.Write(
                status: status,
                code: problem?.Code ?? ErrorCode.INTERNAL,
                type: type,
                correlationId: correlationId,
                details: details,
                errors: errors);

            context.Response.ContentType = "application/problem+json";
            context.Response.StatusCode = status;
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }
}