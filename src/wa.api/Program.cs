using Microsoft.Data.SqlClient;
using Serilog;
using wa.infrastructure.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

// T-006b: event backbone (EF outbox + Service Bus publisher). No new packages.
builder.Services.AddEventPublishing(builder.Configuration);

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
