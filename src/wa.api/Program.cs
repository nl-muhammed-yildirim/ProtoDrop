using System.Diagnostics;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
// TA-10.5: console sink locally (App Insights later per environment); Microsoft.* → Warning, app at Information.
builder.Host.UseSerilog((context, cfg) => cfg
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .WriteTo.Console());
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly));
builder.Services.AddSingleton<wa.application.Ports.IDbHealthProbe, WaDbHealthProbe>();
builder.Services.AddSingleton<wa.application.Ports.ISbHealthProbe, SbHealthProbe>();
// T-054-03: adapter-owned contexts (no request scope in the current API surface); the IEventPublisher switch lands in T-054-05.
builder.Services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
    .UseSqlServer(builder.Configuration.GetConnectionString("WaDb")).Options);
builder.Services.AddSingleton<wa.infrastructure.Persistence.AppDbContextFactory>();
var app = builder.Build();
app.UseSerilogRequestLogging(); // per-request INF line (Microsoft.* request logging is suppressed to Warning)
app.MapGet("/", () => "Hello World!");
app.MapGet("/health", async (ISender sender, CancellationToken ct) =>
{
    try
    {
        var result = await sender.Send(new HealthQuery(), ct);
        return Results.Json(new { status = result.Status, db = result.Db, sb = result.Sb });
    }
    catch (Exception)
    {
        // Reason is logged by the handler (T-050-04). Body is TA-4.1.3 Problem+JSON; code list is closed — no DB_DOWN, so INTERNAL.
        var correlationId = (Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n"))[..8];
        return Results.Json(new
        {
            type = "https://example.com/errors/internal",
            title = "Internal server error",
            status = 503,
            code = "INTERNAL",
            details = "A required component is unreachable.",
            correlationId
        }, statusCode: 503);
    }
});

app.Run();

// Empty partial class so WebApplicationFactory<Program> can resolve the entry point.
public partial class Program { }
