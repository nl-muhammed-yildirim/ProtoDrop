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
    var result = await sender.Send(new HealthQuery(), ct);
    return Results.Json(new { status = result.Status, db = result.Db, sb = result.Sb });
});

app.Run();

// Empty partial class so WebApplicationFactory<Program> can resolve the entry point.
public partial class Program { }
