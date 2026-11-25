using AspNetCoreRateLimit;
using System.Globalization;
using wa.api.Middleware;
using Serilog;
using Serilog.Events;
using wa.api;
using wa.api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
// TA-10.5: console sink locally (App Insights later per environment); Microsoft.* and AspNetCoreRateLimit.* → Warning (TA-9.4: the limiter's blocked-request INF line carries the raw client IP), app at Information.
builder.Host.UseSerilog((context, cfg) => cfg
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("AspNetCoreRateLimit", LogEventLevel.Warning)
    .WriteTo.Console(formatProvider: CultureInfo.CurrentCulture));
builder.Services.AddApiServices(builder.Configuration);
var app = builder.Build();

// T-011-01 (TA-9.2 / FR-011-1): the admin / operator email sets are read once, right after Build() — see AdminGate.
var adminGate = AdminGate.FromConfiguration(builder.Configuration);

// TA-4.1.6 (T-056-03): allow-list CORS only — configured origins via Wa:Cors:AllowedOrigins, no wildcard, preflight cached 1 h.
app.UseMiddleware<CorsAllowListMiddleware>();
app.UseMiddleware<CorrelationMiddleware>(); // FR-056-1: runs before everything else — correlationId present in every log line incl. UseSerilogRequestLogging's per-request INF line
// Rate limiting sits after correlation so the 429 body gets a correlationId, and BEFORE GlobalError so its empty-body 404 rewrite can't touch the 429 (the limiter short-circuits before routing).
app.UseIpRateLimiting();
// T-008-02 (TA-4.1.5): session middleware — after idempotency, before GlobalError so refreshed cookies + error bodies keep correlationId.
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<SessionMiddleware>(); // TA-9.1: reads wa.session cookie → sets HttpContext.Items['wa.session.userId']; refreshes < 15 d remaining (FR-008-2)
app.UseMiddleware<GlobalErrorMiddleware>(); // FR-056-3: every failure → TA-4.1.3 Problem+JSON (business codes keep their status, others 500 INTERNAL)
app.UseMiddleware<PlanContextMiddleware>(); // FR-020-1 / US-020-01: binds session user → PlanContext (guest → Free); resolution happens on first read
app.UseSerilogRequestLogging(options => options.GetMessageTemplateProperties = LogRoute.RequestLogProperties); // per-request INF line (Microsoft.* request logging is suppressed to Warning); TA-9.4: RequestPath = route template (/unsubscribe/{token}), never the raw path

// CHORE-code-style-2 (TA-2.1 / TA-2.4): the endpoint groups, registered in the former Program.cs order.
app.MapSystemEndpoints(app.Environment.IsDevelopment());
app.MapTransferWriteEndpoints();
app.MapPublicTransferEndpoints();
app.MapUnsubscribeEndpoints();
app.MapAdminEndpoints(adminGate, builder.Configuration);
app.MapAuthEndpoints(adminGate);
app.MapMyTransferEndpoints();
app.MapAccountEndpoints();
app.MapBillingEndpoints(builder.Configuration);
app.MapFeatureEndpoints();
app.Run();

/// <summary>Empty partial class so WebApplicationFactory&lt;Program&gt; can resolve the entry point.</summary>
public partial class Program { }
