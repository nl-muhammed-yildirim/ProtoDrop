using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using wa.application.Events;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Billing;
using wa.application.UseCases.DataExport;
using wa.application.UseCases.Gdpr;
using wa.application.UseCases.Transfers;
using wa.infrastructure.Blob;
using wa.infrastructure.Email;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.infrastructure.Telemetry;

namespace wa.workers;

/// <summary>T-006-07: wa.workers DI composition; every setting is read through IConfiguration so Linux "Wa__X" app settings work (TA-13.1).</summary>
public static class WorkerServices
{
    public const string DbKey = "WaDb";
    public const string BlobAccountUrlKey = "Wa:Blob:AccountUrl";
    public const string BlobAccountKeyKey = "Wa:Blob:AccountKey";
    public const string JwtSecretKey = "Wa:Jwt:Secret";
    public const string ServiceBusKey = "Wa:ServiceBus:ConnectionString";

    public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        var dbConnection = configuration[DbKey] ?? "";

        // DbContext for the zip function
        services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(dbConnection).Options);
        services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>()));

        // Blob store
        var blobAccountUrl = Require(configuration, BlobAccountUrlKey);
        var blobAccountKey = Require(configuration, BlobAccountKeyKey);
        services.AddSingleton<IBlobStore>(sp => new AzureBlobStore(
            new Uri(blobAccountUrl), blobAccountKey, sp.GetRequiredService<ILogger<AzureBlobStore>>()));

        // Blob SAS minter
        services.AddSingleton<IBlobSasMinter>(new DraftBlobSasMinter(new Uri(blobAccountUrl), blobAccountKey));

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
        var hmacKey = System.Text.Encoding.UTF8.GetBytes(configuration[JwtSecretKey] ?? "");
        services.AddSingleton<wa.application.Telemetry.WaTelemetryReporter>(sp => new wa.application.Telemetry.WaTelemetryReporter(
            sp.GetRequiredService<ILogger<wa.application.Telemetry.WaTelemetryReporter>>(), hmacKey));

        // DbContext factory (for SbEventPublisher outbox writes)
        services.AddSingleton<IDbContextFactory<WaDbContext>>(new AppDbContextFactory(
            new DbContextOptionsBuilder<WaDbContext>()
                .UseSqlServer(dbConnection).Options));

        // Event publisher: SB in prod, in-memory fallback for local dev (TA-0.2 rule 7).
        var sbConnString = configuration[ServiceBusKey];
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

        // US-019-04 (D-6): billing email sub-step of the f-expire pass — per-step JobRun claims, no window claim.
        services.AddSingleton<IBillingSequenceStore>(sp => new BillingSequenceStore(
            sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<SendBillingEmailsHandler>(sp => new SendBillingEmailsHandler(
            sp.GetRequiredService<IBillingSequenceStore>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<ILogger<SendBillingEmailsHandler>>()));

        // US-019-05 (D-4): billing grace sub-step of the f-expire pass — locked re-check + JobRun claim per grace.
        services.AddSingleton<IBillingGraceStore>(sp => new BillingGraceStore(
            sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<EndBillingGraceHandler>(sp => new EndBillingGraceHandler(
            sp.GetRequiredService<IBillingGraceStore>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<EndBillingGraceHandler>>()));

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

        // US-047-01 (FR-047-1/2): f-data-export builder — JobRun window claim, purge then build; no events (FR-047-8).
        services.AddSingleton<IDataExportJobStore>(sp => new DataExportJobStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<BuildDataExportsHandler>(sp => new BuildDataExportsHandler(
            sp.GetRequiredService<IDataExportJobStore>(),
            sp.GetRequiredService<IBlobStore>(),
            sp.GetRequiredService<ILogger<BuildDataExportsHandler>>()));

        // US-047-02 (FR-047-5): f-gdpr-sweep — nightly, idempotent per userId; option-A eraser (contract by construction, DECISION 2026-11-18).
        services.AddSingleton<IGdprSweepStore>(sp => new GdprSweepStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton<ITelemetryEraser, ClosedSetTelemetryEraser>();
        services.AddSingleton<SweepErasedTelemetryHandler>(sp => new SweepErasedTelemetryHandler(
            sp.GetRequiredService<IGdprSweepStore>(),
            sp.GetRequiredService<ITelemetryEraser>(),
            sp.GetRequiredService<ILogger<SweepErasedTelemetryHandler>>()));

        // T-006-01: f-email consumer
        services.AddSingleton<EventDeduplicator>(sp => new EventDeduplicator(() => DateTimeOffset.UtcNow));
        services.AddSingleton<IEmailStore>(sp => new EmailStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<ITemplateRenderer>(sp => new StubbleTemplateRenderer(
            sp.GetRequiredService<ILogger<StubbleTemplateRenderer>>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>())); // T-014-04: fallback telemetry event

        // IEmailSender selection (TA-0.2 rule 7): log if Wa:Email:Sender=="log" else CommunicationHubEmailSender.
        // Keys are read via IConfiguration (Wa__... on Linux hosts); a missing Wa:CommunicationHub:Connection
        // throws on first resolution (T-006-06 D-3), never a silent fallback to log.
        services.AddSingleton<IEmailSender>(sp => EmailSenderSelector.Create(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton<SendNotificationEmailsHandler>(sp => new SendNotificationEmailsHandler(
            sp.GetRequiredService<IEmailStore>(),
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<ITemplateRenderer>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<EventDeduplicator>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<SendNotificationEmailsHandler>>()));

        // T-008-04 (D-6): magic-link f-email handler — self-contained, loads template from Emails/templates/magic-link/.
        services.AddSingleton<SendMagicLinkEmailHandler>(sp => new SendMagicLinkEmailHandler(
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<SendMagicLinkEmailHandler>>()));

        // T-008-05 (D-3): forgot-password f-email handler — same shape as magic-link, template from Emails/templates/forgot-password/.
        services.AddSingleton<SendForgotPasswordEmailHandler>(sp => new SendForgotPasswordEmailHandler(
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<SendForgotPasswordEmailHandler>>()));

        // US-019-04 (D-7): billing email f-email handler — templates from Emails/templates/billing-payment-failed/.
        services.AddSingleton<SendBillingEmailHandler>(sp => new SendBillingEmailHandler(
            sp.GetRequiredService<IEmailSender>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<wa.application.Telemetry.WaTelemetryReporter>(),
            sp.GetRequiredService<ILogger<SendBillingEmailHandler>>(),
            Path.Combine(AppContext.BaseDirectory, "Emails", "templates")));

        return services;
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{key} is required.") : value;
    }
}
