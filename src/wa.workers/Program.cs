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
using wa.infrastructure.Events;
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

        // DbContext factory (for SbEventPublisher outbox writes)
        services.AddSingleton<IDbContextFactory<WaDbContext>>(new AppDbContextFactory(
            new DbContextOptionsBuilder<WaDbContext>()
                .UseSqlServer(Environment.GetEnvironmentVariable("WaDb") ?? "").Options));

        // Event publisher: SB in prod, in-memory fallback for local dev (TA-0.2 rule 7).
        var sbConnString = Environment.GetEnvironmentVariable("Wa:ServiceBus:ConnectionString");
        if (!string.IsNullOrEmpty(sbConnString))
            services.AddSingleton<IEventPublisher>(sp => new SbEventPublisher(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<SbEventPublisher>>(),
                sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        else
            services.AddSingleton<IEventPublisher>(new InMemoryEventPublisher());

        // T-005-01: f-expire job
        services.AddSingleton<IExpiryStore>(sp => new ExpiryStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<ExpireTransfersHandler>(sp => new ExpireTransfersHandler(
            sp.GetRequiredService<IExpiryStore>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<ExpireTransfersHandler>>()));

        // T-005-02: f-delete-transfers job
        services.AddSingleton<IExpiryDeletionStore>(sp => new ExpiryDeletionStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<DeleteExpiredTransfersHandler>(sp => new DeleteExpiredTransfersHandler(
            sp.GetRequiredService<IExpiryDeletionStore>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IBlobStore>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<DeleteExpiredTransfersHandler>>()));

        // T-005-03: f-delete-blobs job
        services.AddSingleton<IBlobDeletionStore>(sp => new BlobDeletionStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<DeleteOrphanBlobsHandler>(sp => new DeleteOrphanBlobsHandler(
            sp.GetRequiredService<IBlobDeletionStore>(),
            sp.GetRequiredService<IBlobStore>(),
            sp.GetRequiredService<ILogger<DeleteOrphanBlobsHandler>>()));

        // T-006-01: f-email consumer
        services.AddSingleton<EventDeduplicator>(sp => new EventDeduplicator(() => DateTimeOffset.UtcNow));
        services.AddSingleton<IEmailStore>(sp => new EmailStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<ITemplateRenderer>(sp => new StubbleTemplateRenderer());

        // IEmailSender selection: log if Wa:Email:Sender=="log" else CommunicationHubEmailSender (TA-0.2 rule 7).
        var emailSenderType = Environment.GetEnvironmentVariable("Wa:Email:Sender");
        if (emailSenderType == "log")
            services.AddSingleton<IEmailSender>(sp => new LogEmailSender(sp.GetRequiredService<ILogger<LogEmailSender>>()));
        else
            services.AddSingleton<IEmailSender>(sp => new CommunicationHubEmailSender(
                Environment.GetEnvironmentVariable("Wa:Email:FromAddress") ?? "no-reply@protodrop.com",
                sp.GetRequiredService<ILogger<CommunicationHubEmailSender>>()));

        services.AddSingleton<SendNotificationEmailsHandler>(sp => new SendNotificationEmailsHandler(
            sp.GetRequiredService<IEmailStore>(),
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<ITemplateRenderer>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<EventDeduplicator>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<SendNotificationEmailsHandler>>()));
    })
    .Build();

await host.RunAsync();
