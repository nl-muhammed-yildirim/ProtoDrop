using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using wa.api.Middleware;
using wa.application.UseCases.Transfers;
using wa.domain.Errors;

namespace wa.api.integration;

/// <summary>
/// T-012-07 (TA-9.4, EC-012-3 / AC-012-3): no PII through neutral log slots. Middleware-level facts (no containers):
/// the business-error line carries code + status only (no file name), path-shaped log values are the route template
/// (never the unsubscribe token, which decodes to both emails), while the Problem+JSON body stays unchanged (TA-4.1.3).
/// </summary>
public class NeutralLogSlotTests
{
    private const string Victim = "victim@x.com";
    private const string Sender = "boss@y.com";

    private static string NewToken() => UnsubscribeToken.Create(Victim, Sender, Encoding.UTF8.GetBytes("k"));

    private static DefaultHttpContext NewContext(string path, string? routeTemplate)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (routeTemplate is not null)
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(routeTemplate), 0, null, null));
        return context;
    }

    private static string Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task BusinessError_LogLineHasCodeButNoFileName()
    {
        var logger = new CaptureLogger();
        var middleware = new GlobalErrorMiddleware(
            _ => throw new BusinessException(ProblemCodes.Validation, "File 'secret-q3-report.pdf' is 300 bytes on the server but was declared as 100 bytes."),
            logger);
        var context = NewContext("/transfers/abc/finalize", "/transfers/{id}/finalize");

        await middleware.InvokeAsync(context);

        Assert.Contains(logger.Lines, l => l.Contains(ProblemCodes.Validation, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("secret-q3-report", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProblemCodes.StatusOf(ProblemCodes.Validation), context.Response.StatusCode);
        Assert.Contains("secret-q3-report.pdf", Body(context), StringComparison.Ordinal); // D-1: Problem+JSON body untouched
    }

    [Fact]
    public async Task Unhandled_OnTokenRoute_LogsRouteTemplateNotToken()
    {
        var token = NewToken();
        var logger = new CaptureLogger();
        var middleware = new GlobalErrorMiddleware(_ => throw new InvalidOperationException("boom"), logger);
        var context = NewContext("/unsubscribe/" + token, "/unsubscribe/{token}");

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Contains(logger.Lines, l => l.Contains("/unsubscribe/{token}", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, l =>
            l.Contains(token, StringComparison.Ordinal)
            || l.Contains(Victim, StringComparison.OrdinalIgnoreCase)
            || l.Contains(Sender, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unhandled_NoEndpoint_LogsUnmatchedFallback()
    {
        var token = NewToken();
        var logger = new CaptureLogger();
        var middleware = new GlobalErrorMiddleware(_ => throw new InvalidOperationException("boom"), logger);
        var context = NewContext("/unsubscribe/" + token, null);

        await middleware.InvokeAsync(context);

        Assert.Contains(logger.Lines, l => l.Contains(LogRoute.Unmatched, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, l => l.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void RequestLogProperties_RequestPathIsRouteTemplate()
    {
        var token = NewToken();
        var context = NewContext("/unsubscribe/" + token, "/unsubscribe/{token}");

        var props = LogRoute.RequestLogProperties(context, "/unsubscribe/" + token, 1.5, 200).ToList();

        var path = Assert.IsType<ScalarValue>(Assert.Single(props, p => p.Name == "RequestPath").Value);
        Assert.Equal("/unsubscribe/{token}", path.Value);
        Assert.DoesNotContain(props, p => p.Value.ToString().Contains(token, StringComparison.Ordinal));
    }

    private sealed class CaptureLogger : ILogger<GlobalErrorMiddleware>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
