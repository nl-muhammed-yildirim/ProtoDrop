using System.Diagnostics;
using MediatR;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly));
builder.Services.AddSingleton<wa.application.Ports.IDbHealthProbe, WaDbHealthProbe>();
builder.Services.AddSingleton<wa.application.Ports.ISbHealthProbe, SbHealthProbe>();
var app = builder.Build();
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
