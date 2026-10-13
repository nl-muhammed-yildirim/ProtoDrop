using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder; // IStartupFilter — the only test-only injection point that runs inside Program's own Build() (AC-056-2 pipeline-ordering invariant)
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing; // shared EndpointDataSource + RouteEndpoint — see ThrowingRoutesFilter
using Microsoft.AspNetCore.Http; // Endpoint base type — shared by RouteEndpoint and the EndpointDataSource cast
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost; // TestServer.SendAsync(HttpRequestMessage) — raw wire-level response headers
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging; // LoggerFactory — the M.E.L bridge in test 3
using Serilog;
using Serilog.Core;
using Serilog.Events;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using Xunit;

namespace wa.api.integration;

// AC-056-1/2/4 (T-056-05 — T-008 exit checks): Problem+JSON with correlationId for 404/500, no stack in the body, and an upload_started-style event through WaTelemetryReporter with the exact TA-10.2 template name + properties.
public class PipelineTests
{
    // Program.cs builds the blob client eagerly from §5.2 config (T-055-02) — env vars are always visible to CreateBuilder's configuration, same pattern as HealthFailureTests.
    static PipelineTests()
    {
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", "http://127.0.0.1:10000/devstoreaccount1");
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");
    }

    private sealed class ThrowingRouteFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            // IStartupFilter wraps the app-startup action inside Program's own Build(), so its MapGet lands in the real routing pipeline — executed INSIDE Correlation/GlobalError (the AC-056-2 invariant: the throw must be caught by GlobalErrorMiddleware, not bubble to the client). A classic Map() at head or an IHostedService mapper can't reach this point: .NET 10 minimal APIs never register IEndpointRouteBuilder in DI.
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(sp => new ThrowingRoutesFilter()));
    }

    private sealed class ThrowingRoutesFilter : IStartupFilter
    {
        // The filter's next delegate is Program's own Build body — Correlation, GlobalError and the real routes all land in the pipeline first, so this Map call (below) sits INSIDE every middleware. That's the AC-056-2 invariant: GlobalErrorMiddleware must catch the throw. A classic builder.Configure Map() lands at HEAD; an IHostedService mapper runs after Build with read-only services.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/pipeline-test/throw", (Action<IApplicationBuilder>)(b => b.Run(async ctx => throw new InvalidOperationException("T-056-05 forced failure — stack stays in Serilog, out of the body"))));
        };
    }

    private readonly ThrowingRouteFactory _factory = new();

    [Fact]
    public async Task UnknownRoute_Returns404ProblemJson_BodyCorrelationIdMatchesLogContextValue()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/no-such-route-m0");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // FR-056-2: the middleware rewrites minimal APIs' empty-body 404 — the body is ours now
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        // Exact TA-4.1.3 field set for a 404 — names AND order pinned (US-056-02 first Gherkin block; JsonObject preserves insertion order, ProblemWriter.Build is the single construction site).
        Assert.Equal(["type", "title", "status", "code", "details", "correlationId"], root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("NOT_FOUND", root.GetProperty("code").GetString());
        Assert.Equal(404, root.GetProperty("status").GetInt32());

        // The body's correlationId IS the log-context value for this request: CorrelationMiddleware pushes it into Serilog's context, and that same 8-hex prefix is what the TraceParent response header carries (TA-10.1 "traceparent in → out").
        var correlationId = root.GetProperty("correlationId").GetString()!;
        Assert.Matches(new Regex(@"^[0-9a-f]{8}$"), correlationId);
        var traceparent = response.Headers.GetValues("traceparent").Single(); // raw header text as written on the wire
        Assert.StartsWith(correlationId, traceparent.Split('-', 4)[1]); // same trace id the log context holds — every Serilog line for this request carries it via FromLogContext enrichment (TA-10.5)
    }

    [Fact]
    public async Task ForcedUnhandledException_Returns500ProblemJson_NoStackTraceInBody()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("/pipeline-test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); // any non-BusinessException → 500 INTERNAL (TA-4.1.3 closed list — no DB_DOWN-style ad-hoc codes)
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("INTERNAL", root.GetProperty("code").GetString());
        Assert.Equal(500, root.GetProperty("status").GetInt32());

        // TA-10.5: the full stack stays in Serilog — none of it may leak into the body (PII + internal detail).
        Assert.DoesNotContain("at ", body); // no C# stack frames ("   at …")
        Assert.DoesNotContain("T-056-05 forced failure", body); // not even our exception message leaks
    }

    [Fact]
    public void UploadStartedStyleEvent_ThroughWaTelemetryReporter_ProducesTheExactTa102TemplateNameAndNoOtherProperties()
    {
        var sink = new RecordingSink();
        using var serilog = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger(); // real Serilog pipeline (transitive via Microsoft.AspNetCore.Mvc.Testing — no collector package)

        using var melFactory = LoggerFactory.Create(p => p.AddSerilog(serilog)); // the M.E.L → Serilog bridge UseSerilog uses; only our sink sees this event
        var reporter = new WaTelemetryReporter(melFactory.CreateLogger<WaTelemetryReporter>(), System.Text.Encoding.UTF8.GetBytes("Wa:Jwt:Secret test key"));

        reporter.LogEvent(TelemetryEvents.UploadStarted); // the upload_started-style event AC-056-4 names — a plain-string M.E.L overload, so the template text IS exactly the closed-set name

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal("upload_started", logEvent.MessageTemplate.ToString()); // no renames: wire value == TA-10.2 literal — App Insights queries by this exact name
        // The M.E.L bridge enriches every event with SourceContext; upload_started has zero TA-10.2 contract properties (TA line 816) — pin the exact key set so the App Insights query shape stays stable.
        Assert.Single(logEvent.Properties);
        Assert.True(logEvent.Properties.ContainsKey("SourceContext"));
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent); // the template + properties are already resolved at this point — nothing to capture from args
    }
}
