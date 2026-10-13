using System.Diagnostics;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;
using Serilog;
using Serilog.Context;
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
// Adapter-owned contexts (no request scope in the current API surface).
builder.Services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
    .UseSqlServer(builder.Configuration.GetConnectionString("WaDb")).Options);
builder.Services.AddSingleton<wa.infrastructure.Persistence.AppDbContextFactory>();
// T-054-05 (TA-0.2 rule 7): no "is this the fake?" branch in production code — DI picks the adapter on the connection string.
var sbConnString = builder.Configuration["Wa:ServiceBus:ConnectionString"];
if (string.IsNullOrEmpty(sbConnString))
    builder.Services.AddSingleton<wa.application.Ports.IEventPublisher, InMemoryEventPublisher>();
else
    builder.Services.AddSingleton<wa.application.Ports.IEventPublisher>(sp => new SbEventPublisher(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<SbEventPublisher>>(),
        sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
// T-055-02 (TA-3.5 topology, §5.2 config): blob store + one-time container bootstrap. GR#7 — no "is this the fake?" branch; the adapter is picked by its §5.2 config values only.
var blobAccountUrl = builder.Configuration["Wa:Blob:AccountUrl"];
var blobAccountKey = builder.Configuration["Wa:Blob:AccountKey"];
builder.Services.AddSingleton<wa.application.Ports.IBlobStore>(sp => new AzureBlobStore(
    new Uri(blobAccountUrl!), blobAccountKey!, sp.GetRequiredService<ILogger<AzureBlobStore>>()));
// BlobContainerBootstrap (TA-3.5) needs a raw client for service-level properties; account name via the adapter's helper.
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri(blobAccountUrl!),
    new StorageSharedKeyCredential(AzureBlobStore.AccountNameFrom(new Uri(blobAccountUrl!)), blobAccountKey!)));
builder.Services.AddHostedService<BlobContainerBootstrap>();
var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>(); // FR-056-1: runs before everything else — correlationId present in every log line incl. UseSerilogRequestLogging's per-request INF line
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
