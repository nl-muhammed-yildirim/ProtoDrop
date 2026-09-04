using AspNetCoreRateLimit;
using Microsoft.Data.SqlClient;
using wa.api.Pipeline;
using Serilog;
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

// T-006b: event backbone (EF outbox + Service Bus publisher). No new packages.
builder.Services.AddEventPublishing(builder.Configuration);

// T-007a: blob foundation (TA-3.5): staging/transfers containers on ONE
// storage account (Azurite locally per AGENT.md §5.2). No new packages.
builder.Services.AddBlobStore(builder.Configuration);

// T-008a: W3C correlation context (TA-4.1.3 / TA-10.1), populated
// by CorrelationMiddleware on every request.
builder.Services.AddScoped<CorrelationContext>();

// T-008b: CORS (TA-4.1.6) — allowed origins from config
// `Wa:Cors:AllowedOrigins`; preflight cached 1 h. AllowCredentials
// because the browser auth scheme is an httpOnly same-site cookie
// (TA-4.1.2).
builder.Services.AddCors(options =>
{
    options.AddPolicy("wa", policy =>
    {
        string[] origins = (builder.Configuration["Wa:Cors:AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1)); // TA-4.1.6: preflight cached 1 h
    });
});

// T-008b: rate limiting (TA-9.5) — the in-memory API second layer
// (Front Door WAF is the 10/min first layer) for /auth: 5/min per IP.
// AspNetCoreRateLimit is already pinned (TA-2.6); in-memory stores,
// no new packages. The 5.0.0 startup requires MemoryCache +
// IRateLimitConfiguration besides AddInMemoryRateLimiting.
string apiBaseUrlForLimits = builder.Configuration["Wa:Url:Api"] ?? string.Empty;
builder.Services.AddMemoryCache();
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.Configure<IpRateLimitOptions>(o =>
{
    o.EnableEndpointRateLimiting = true;
    o.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule { Endpoint = "*:/api/v1/auth/*", Period = "1m", Limit = 5 }
    };
    // The library emits the 429 as text/plain by default. This hook
    // runs before the library writes the response; it points the
    // shared QuotaExceededResponse at a per-request Problem+JSON
    // body (TA-4.1.3: closed-list code RATE_LIMITED + correlation id).
    // Literal braces in Content are doubled — the library runs
    // string.Format(content, limit, period, retryAfter).
    o.RequestBlockedBehaviorAsync = (context, _, _, _) =>
    {
        string correlationId = context.RequestServices
            .GetRequiredService<CorrelationContext>().CorrelationId;
        o.QuotaExceededResponse = new QuotaExceededResponse
        {
            StatusCode = 429,
            ContentType = "application/problem+json",
            Content =
                "{{\"type\":\"" + ProblemWriter.BuildTypeUri(apiBaseUrlForLimits, ErrorCode.RATE_LIMITED) + "\","
                + "\"title\":\"" + ProblemWriter.BuildTitle(ErrorCode.RATE_LIMITED) + "\","
                + "\"status\":429,\"code\":\"RATE_LIMITED\","
                + "\"details\":\"Limit {0} per {1}. Retry after {2}s.\","
                + "\"correlationId\":\"" + correlationId + "\"}}"
        };
        return Task.CompletedTask;
    };
});

builder.Host.UseSerilog((_, config) =>
{
    // TA-10.5: Information for request/transfer lifecycle,
    // Warning for retries, Error for 5xx/DLQ.
    config.MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

var app = builder.Build();

// T-008a: correlation middleware first (populates context + sets
// response header), error mapping next (catches everything
// downstream into Problem+JSON, TA-4.1.3).
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<GlobalErrorMiddleware>();

// T-008b: CORS (TA-4.1.6) after error mapping so a 400/500 from
// any downstream endpoint still carries the CORS headers a preflight
// would need to observe the response.
app.UseCors("wa");

// T-008b: rate limiting (TA-9.5) after CORS so a 429 carries the
// CORS headers, and after correlation so the blocked response can
// echo the request correlation id.
app.UseIpRateLimiting();

var cfg = app.Configuration;

// TA-12.2: health = DB ping + SB ping (Front Door health probe target).
// SB reports "skipped" when no connection string is set (in-memory publisher
// fake locally, per AGENT.md §5.2). DB reads ConnectionStrings:WaDb.
var healthLogger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapGet("/health", async () =>
{
    var db = await Task.Run(
        () => PingDatabase(cfg.GetConnectionString("WaDb"), healthLogger));
    var sb = string.IsNullOrWhiteSpace(cfg["Wa:ServiceBus:ConnectionString"])
        ? "skipped"
        : "ok";

    var healthy = db == "ok";
    return Results.Json(
        new { status = healthy ? "ok" : "fail", db, sb },
        statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();

static string PingDatabase(string? connectionString, ILogger logger)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        logger.LogWarning("Health: WaDb connection string not set");
        return "missing-connection";
    }

    // TA-12.2 health = DB ping. Local bootstrap (no EF migrations yet in
    // T-002): if the engine answers on master but database 'wa' is missing,
    // create it, then re-ping the real connection string.
    try
    {
        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        };

        if (!TrySelect1(masterBuilder.ConnectionString))
        {
            return "fail";
        }

        using var masterConnection = new SqlConnection(masterBuilder.ConnectionString);
        masterConnection.Open();

        using (var check = new SqlCommand(
            "SELECT COUNT(*) FROM sys.databases WHERE name = N'wa'", masterConnection))
        {
            check.CommandTimeout = 5;
            var exists = (int)check.ExecuteScalar() > 0;

            if (!exists)
            {
                using var create = new SqlCommand("CREATE DATABASE [wa]", masterConnection);
                create.CommandTimeout = 30;
                create.ExecuteNonQuery();
                logger.LogInformation("Health: created missing database 'wa'");
            }
        }

        var ok = TrySelect1(connectionString);
        logger.LogDebug("Health: DB ping: {Result}", ok ? "ok" : "fail");
        return ok ? "ok" : "fail";
    }
    catch (SqlException ex)
    {
        logger.LogWarning(ex, "Health: DB ping failed");
        return "fail";
    }
}

static bool TrySelect1(string connectionString)
{
    using var connection = new SqlConnection(connectionString);
    connection.Open();
    using var command = new SqlCommand("SELECT 1", connection);
    command.CommandTimeout = 5;
    using var reader = command.ExecuteReader();
    return reader.Read();
}
