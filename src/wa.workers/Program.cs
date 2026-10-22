using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.infrastructure.Blob;
using wa.infrastructure.Persistence;

var host = new HostBuilder()
    .ConfigureAppConfiguration(config => config.AddEnvironmentVariables())
    .ConfigureServices(services =>
    {
        // DbContext for the zip function
        services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable("WaDb") ?? "").Options);
        services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>()));

        // Blob store
        var blobAccountUrl = Environment.GetEnvironmentVariable("Wa:Blob:AccountUrl");
        var blobAccountKey = Environment.GetEnvironmentVariable("Wa:Blob:AccountKey");
        services.AddSingleton<IBlobStore>(sp => new AzureBlobStore(
            new Uri(blobAccountUrl!), blobAccountKey!, sp.GetRequiredService<ILogger<AzureBlobStore>>()));

        // Blob SAS minter
        services.AddSingleton<IBlobSasMinter>(new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!));

        // Limits provider
        var limitsTtl = TimeSpan.FromSeconds(30);
        services.AddSingleton<IPlansStore>(sp => new PlansStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<IFlagsStore>(sp => new FlagsStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton(sp => new PlansCache(sp.GetRequiredService<IPlansStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
        services.AddSingleton(sp => new FlagsCache(sp.GetRequiredService<IFlagsStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
        services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));

        // Zip file source
        services.AddSingleton<IZipFileSource>(sp => new ZipFileSource(sp.GetRequiredService<WaDbContext>()));

        // Telemetry reporter
        var hmacKey = System.Text.Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("Wa:Jwt:Secret") ?? "");
        services.AddSingleton<wa.application.Telemetry.WaTelemetryReporter>(sp => new wa.application.Telemetry.WaTelemetryReporter(
            sp.GetRequiredService<ILogger<wa.application.Telemetry.WaTelemetryReporter>>(), hmacKey));
    })
    .Build();

await host.RunAsync();
