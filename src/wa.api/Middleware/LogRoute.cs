using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog.Events;

namespace wa.api.Middleware;

/// <summary>
/// Owns "what path-shaped value may be logged" (TA-9.4): the matched route template (e.g. <c>/unsubscribe/{token}</c>), never the raw
/// request path — which can carry a token that decodes to emails. Route templates are compile-time constants, so never PII.
/// </summary>
public static class LogRoute
{
    /// <summary>Logged when no endpoint matched — arbitrary paths can carry anything, so the raw path is dropped.</summary>
    public const string Unmatched = "(unmatched)";

    /// <summary>The matched route template, or <see cref="Unmatched"/>.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The resulting text.</returns>
    public static string Of(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? Unmatched;

    /// <summary>
    /// Serilog request-logging properties (default template <c>HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed}</c>)
    /// with <c>RequestPath</c> = route template. The raw <paramref name="requestPath"/> is ignored on purpose (TA-9.4).
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="requestPath">The request path.</param>
    /// <param name="elapsedMs">The elapsed ms.</param>
    /// <param name="statusCode">The status code.</param>
    /// <returns>The matching log event property items.</returns>
    public static IEnumerable<LogEventProperty> RequestLogProperties(HttpContext context, string requestPath, double elapsedMs, int statusCode) =>
    [
        new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
        new LogEventProperty("RequestPath", new ScalarValue(Of(context))),
        new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
        new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
    ];
}
