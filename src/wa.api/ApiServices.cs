using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;

namespace wa.api;

/// <summary>CHORE-code-style-2 (TA-2.3): the API composition root — every dependency-injection registration that used to sit at the top of Program.cs.</summary>
public static class ApiServices
{
    /// <summary>
    /// Registers the API's services in exactly the former Program.cs order (the last registration wins, enumerable order matters).
    /// Configuration is read at the same moments as before: eager reads here, per-request reads inside the registered lambdas.
    /// </summary>
    /// <param name="services">The service collection of the web application builder.</param>
    /// <param name="configuration">The live configuration of the web application builder.</param>
    /// <returns>The same <paramref name="services"/>, so calls can be chained.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly));
        services.AddSingleton(TimeProvider.System); // CHORE-code-style-2: the one clock — handlers/stores take TimeProvider instead of reading DateTime.UtcNow
        services.AddSingleton<wa.application.Ports.IDbHealthProbe, WaDbHealthProbe>();
        services.AddSingleton<wa.application.Ports.ISbHealthProbe, SbHealthProbe>();
        // Adapter-owned contexts (no request scope in the current API surface).
        services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(configuration.GetConnectionString("WaDb")).Options);
        services.AddSingleton<wa.infrastructure.Persistence.AppDbContextFactory>();
        // SbEventPublisher (T-054-03) resolves the interface — register under both types.
        services.AddSingleton<IDbContextFactory<WaDbContext>, wa.infrastructure.Persistence.AppDbContextFactory>();
        // T-054-05 (TA-0.2 rule 7): no "is this the fake?" branch in production code — DI picks the adapter on the connection string.
        var sbConnString = configuration["Wa:ServiceBus:ConnectionString"];
        if (string.IsNullOrEmpty(sbConnString))
            services.AddSingleton<wa.application.Ports.IEventPublisher, InMemoryEventPublisher>();
        else
            services.AddSingleton<wa.application.Ports.IEventPublisher>(sp => new SbEventPublisher(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<SbEventPublisher>>(),
                sp.GetRequiredService<IDbContextFactory<WaDbContext>>(), sp.GetRequiredService<TimeProvider>()));
        // T-055-02 (TA-3.5 topology, §5.2 config): blob store + one-time container bootstrap. GR#7 — no "is this the fake?" branch; the adapter is picked by its §5.2 config values only.
        var blobAccountUrl = configuration["Wa:Blob:AccountUrl"];
        var blobAccountKey = configuration["Wa:Blob:AccountKey"];
        services.AddSingleton(sp => new AzureBlobStore(
            new Uri(blobAccountUrl!), blobAccountKey!, sp.GetRequiredService<ILogger<AzureBlobStore>>()));
        services.AddSingleton<wa.application.Ports.IBlobStore>(sp => sp.GetRequiredService<AzureBlobStore>());
        services.AddSingleton<wa.application.Ports.IBlobContentWriter>(sp => sp.GetRequiredService<AzureBlobStore>()); // US-021-04: logo upload with Content-Type
        // BlobContainerBootstrap (TA-3.5) needs a raw client for service-level properties; account name via the adapter's helper.
        services.AddSingleton(new BlobServiceClient(
            new Uri(blobAccountUrl!),
            new StorageSharedKeyCredential(AzureBlobStore.AccountNameFrom(new Uri(blobAccountUrl!)), blobAccountKey!)));
        services.AddHostedService<BlobContainerBootstrap>();
        // T-001-05 (TA-4.2#1): limits resolution + draft SAS minting — every read through the caches (FR-053-4); guest plan resolves via seeded rows, never literals.
        services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>())); // adapter-owned context; no request scope in the current API surface (same style as L29 above).
        var limitsTtl = TimeSpan.FromSeconds(int.TryParse(configuration["Wa:Flags:TtlSeconds"], out var ttlSecs) ? ttlSecs : 30); // TA-3.4 — both caches share this value (Caches doc comment).
        services.AddSingleton<IPlansStore>(sp => new PlansStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<IFlagsStore>(sp => new FlagsStore(sp.GetRequiredService<WaDbContext>()));
        // T-001-10 (TA-4.2#2): finalize handler's DB surface — thin adapter over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Transfers.ITransferStore>(sp => new wa.infrastructure.Persistence.TransferStore(sp.GetRequiredService<WaDbContext>()));
        // US-045-01 (FR-045-1/3/7): owner-scoped transfer search port — D-21 option B, literal substring match over FileItem.OriginalName / EmailRecipient.Address / Transfer.SenderName.
        services.AddSingleton<wa.application.UseCases.Transfers.ITransferSearchStore>(sp => new wa.infrastructure.Persistence.TransferSearchStore(sp.GetRequiredService<WaDbContext>())); // US-045-01
        // T-002-04 (TA-4.2#3): send handler's DB surface — thin adapter over the adapter-owned WaDbContext.
        // T-010-02: IBlobStore (the gate primitive, registered as a singleton at L53) — SendStore's FILES_GONE gate checks physical presence via ExistsAsync.
        services.AddSingleton<wa.application.UseCases.Transfers.ISendStore>(sp => new wa.infrastructure.Persistence.SendStore(sp.GetRequiredService<WaDbContext>(), sp.GetRequiredService<wa.application.Ports.IBlobStore>()));
        // T-014 (TA-4.2#4): recipient-page handler's DB surface — thin adapter over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Transfers.IPublicTransferStore>(sp => new wa.infrastructure.Persistence.PublicTransferStore(sp.GetRequiredService<WaDbContext>(), sp.GetRequiredService<TimeProvider>()));
        // US-023-01 (F-PRF-003): download analytics read port (GET /transfers/{id}/analytics).
        services.AddSingleton<wa.application.UseCases.Transfers.ITransferAnalyticsStore>(sp => new wa.infrastructure.Persistence.TransferAnalyticsStore(sp.GetRequiredService<WaDbContext>()));
        // US-021-01: sender branding read port (org name beside the sender on the recipient page).
        services.AddSingleton<wa.application.UseCases.Transfers.ISenderBrandingStore>(sp => new wa.infrastructure.Persistence.SenderBrandingStore(sp.GetRequiredService<WaDbContext>()));
        // US-021-04: org logo path write port (DELETE /auth/me/logo).
        services.AddSingleton<wa.application.UseCases.Auth.IOrgLogoStore>(sp => new wa.infrastructure.Persistence.OrgLogoStore(sp.GetRequiredService<WaDbContext>()));
        // F-PRF-001: org name write port (PATCH /auth/me orgName).
        services.AddSingleton<wa.application.UseCases.Auth.IOrgNameStore>(sp => new wa.infrastructure.Persistence.OrgNameStore(sp.GetRequiredService<WaDbContext>()));
        // F-XCT-003: AppUser.EmailNotificationsEnabled port (PATCH /auth/me emailNotifications).
        services.AddSingleton<wa.application.UseCases.EmailSettings.IEmailNotificationsStore>(sp => new wa.infrastructure.Persistence.EmailNotificationsStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        // T-002-02 (TA-4.1.5): generic HTTP idempotency store — used by IdempotencyMiddleware for finalize + send endpoints.
        services.AddSingleton<IIdempotencyStore>(sp => new wa.infrastructure.Persistence.IdempotencyStore(sp.GetRequiredService<WaDbContext>(), sp.GetRequiredService<TimeProvider>()));
        // T-011-02 (FR-011-5 / TA-3.2): audit write helper — one AuditLog row + one admin.action domain event per mutation.
        services.AddSingleton<wa.application.UseCases.Admin.IAuditStore>(sp => new wa.infrastructure.Persistence.AuditStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<wa.application.UseCases.Admin.IAuditWriter>(sp => new wa.application.UseCases.Admin.AuditWriter(sp.GetRequiredService<wa.application.UseCases.Admin.IAuditStore>(), sp.GetRequiredService<wa.application.Ports.IEventPublisher>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new PlansCache(sp.GetRequiredService<IPlansStore>(), limitsTtl, sp.GetRequiredService<TimeProvider>().GetUtcNow));
        services.AddSingleton(sp => new FlagsCache(sp.GetRequiredService<IFlagsStore>(), limitsTtl, sp.GetRequiredService<TimeProvider>().GetUtcNow));
        // T-011-03 (TA-4.2#20 / FR-011-6): overview aggregates — store + 5-min snapshot cache (pinned by spec; not config-driven).
        services.AddSingleton<wa.application.UseCases.Admin.IOverviewStore>(sp => new wa.infrastructure.Persistence.OverviewStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton(sp => new wa.application.UseCases.Admin.OverviewSnapshotCache(sp.GetRequiredService<wa.application.UseCases.Admin.IOverviewStore>(), TimeSpan.FromMinutes(5), sp.GetRequiredService<TimeProvider>().GetUtcNow));
        // T-011-04 (TA-4.2#21 / FR-011-3): admin transfer search — store over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Admin.ISearchTransfersStore>(sp => new wa.infrastructure.Persistence.SearchTransfersStore(sp.GetRequiredService<WaDbContext>()));
        // T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — store over the adapter-owned WaDbContext (platform-scoped predicate).
        services.AddSingleton<wa.application.UseCases.Admin.IAdminTransferStore>(sp => new wa.infrastructure.Persistence.AdminTransferStore(sp.GetRequiredService<WaDbContext>()));
        // T-011-06 (TA-4.2#22 / FR-011-2): admin users list — store over the adapter-owned WaDbContext (computed plan/storage/count).
        services.AddSingleton<wa.application.UseCases.Admin.IAdminUserStore>(sp => new wa.infrastructure.Persistence.AdminUserStore(sp.GetRequiredService<WaDbContext>()));
        // T-011-08 (TA-4.2#23 / FR-011-2): admin flag write surface — single-key PK upsert over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Admin.IAdminFlagStore>(sp => new wa.infrastructure.Persistence.AdminFlagStore(sp.GetRequiredService<WaDbContext>()));
        // T-011-09 (TA-4.2#24 / FR-011-2): admin suppressions surface — list + remove over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Admin.IAdminSuppressionStore>(sp => new wa.infrastructure.Persistence.AdminSuppressionStore(sp.GetRequiredService<WaDbContext>()));
        // T-011-09 (TA-4.2#25 / US-011-04 happy path 5): audit read surface — last count rows, newest first, over the adapter-owned WaDbContext.
        services.AddSingleton<wa.application.UseCases.Admin.IAuditReadStore>(sp => new wa.infrastructure.Persistence.AuditReadStore(sp.GetRequiredService<WaDbContext>()));
        // US-047-03 (FR-047-6): erasure report read — fresh context per call (DataExportStore precedent).
        services.AddSingleton<wa.application.UseCases.Admin.IErasureReportStore>(sp => new wa.infrastructure.Persistence.ErasureReportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        services.AddSingleton(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
        services.AddSingleton<ILimitsProvider>(sp => sp.GetRequiredService<LimitsProvider>());
        services.AddSingleton<IFlagResolver>(sp => sp.GetRequiredService<LimitsProvider>()); // US-044-02: phase gates (FR-044-3)
        // T-031-05 (US-018-05, TA-3.4): feature toggles — one resolver over the same caches.
        services.AddSingleton<IFeaturesProvider>(sp => new FeaturesProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
        // US-020-01 (FR-020-1): per-request PlanContext — user→plan-code port (singleton) + scoped accessor bound by PlanContextMiddleware.
        services.AddSingleton<IUserPlanStore>(sp => new wa.infrastructure.Persistence.UserPlanStore(sp.GetRequiredService<WaDbContext>()));
        services.AddScoped<PlanContextAccessor>();
        services.AddScoped<IPlanContextAccessor>(sp => sp.GetRequiredService<PlanContextAccessor>());
        // TA-3.6 — minting is pure HMAC (no network at request time); §5.2 account values already read above (T-055-02).
        var sasMinter = new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!);
        services.AddSingleton<IBlobSasMinter>(sasMinter);
        services.AddSingleton<IExportSasMinter>(sasMinter); // US-047-01: export download SAS (TA-3.6)
        services.AddSingleton<IOrgLogoSasMinter>(sasMinter); // US-021-04: org logo read SAS (TA-3.6)
        services.AddSingleton<wa.application.UseCases.DataExport.IDataExportStore>(sp => new wa.infrastructure.Persistence.DataExportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        // TA-9.5 second line (T-056-03): AspNetCoreRateLimit frozen at 5.0.0, in-memory store — per-instance state is correct at M0 scale; a distributed store would be an ADR change.
        services.AddMemoryCache(); // the package's MemoryCache* stores need IMemoryCache, which this app does not register by default (DI validation caught it)
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>(); // v5.0.0 leaves this to the caller — AsyncKeyLockProcessingStrategy requires it and AddInMemoryRateLimiting registers no implementation
        services.AddInMemoryRateLimiting(); // must run before Build() — the middleware pulls RateLimitConfiguration from DI
        // FR-056-3 (T-056-03): *:/api/v1/auth/* = 5/min per IP → 429 RATE_LIMITED + Retry-After, body via ProblemWriter. The rule's Endpoint keeps the literal `*:` prefix —
        // the processor matches GeneralRules against both `*:{path}` and `{verb}:{path}`, so a plain path would never align (Core/RateLimitProcessor.cs).
        var authRule = new RateLimitRule { Endpoint = "*:/api/v1/auth/*", Period = "1m", Limit = 5 };
        // On breach the library's ReturnQuotaExceededResponse writes AFTER our hook on the same unstarted response — an empty application/json body keeps the Problem payload intact.
        authRule.QuotaExceededResponse = new QuotaExceededResponse { StatusCode = 429, ContentType = "application/json", Content = "" };
        services.Configure<IpRateLimitOptions>(opts =>
        {
            opts.EnableEndpointRateLimiting = true; // required — without it GeneralRules only match Endpoint == "*" (Core/RateLimitProcessor.cs)
            opts.GeneralRules = [authRule];
            // The library sets Retry-After only AFTER our hook returns (RateLimitMiddleware.Invoke → ReturnQuotaExceededResponse), but Kestrel flushes headers with the first body byte — so we set it here, before WriteCompleteAsync commits headers (same formula the library uses: Extensions.RetryAfterFrom).
            opts.RequestBlockedBehaviorAsync = async (context, _, counter, rule) =>
            {
                context.Response.Headers["Retry-After"] = counter.Timestamp.RetryAfterFrom(rule); // set BEFORE WriteCompleteAsync — its StartAsync commits headers before any body byte, so this survives Kestrel's header flush
                await ProblemWriter.WriteCompleteAsync(context, 429, ProblemCodes.RateLimited, "Too many requests — limit is 5 per minute.");
            };
        });
        // AC-056-4 (T-056-04): one reporter for every event — names from the closed TA-10.2 set, template shapes pinned in its doc comment (T-056-05 asserts them).
        services.AddSingleton<wa.application.Telemetry.WaTelemetryReporter>(sp => new wa.application.Telemetry.WaTelemetryReporter(
            sp.GetRequiredService<ILogger<wa.application.Telemetry.WaTelemetryReporter>>(),
            System.Text.Encoding.UTF8.GetBytes(configuration["Wa:Jwt:Secret"]!))); // TA-9.4: the IP HMAC key IS Wa:Jwt:Secret — UTF-8 of the raw string (same key T-008's session cookie signs with, TA-9.1)
        // T-003-04 (TA-4.2#5): unlock-JWT minter — BCL-only HS256, the SAME Wa:Jwt:Secret bytes (TA-9.1: "same key T-008's session cookie signs with").
        // Injectable clock arg is the registered TimeProvider's GetUtcNow in production; unit tests pin exp via their own instance (D3).
        services.AddSingleton<IUnlockTokenMinter>(sp => new UnlockTokenService(
            System.Text.Encoding.UTF8.GetBytes(configuration["Wa:Jwt:Secret"]!), sp.GetRequiredService<TimeProvider>().GetUtcNow));
        // T-008-01 (TA-4.2#8): signup's DB surface — thin adapter over the adapter-owned WaDbContext, and its TA-9.1 session-token minter (30-day rolling).
        services.AddSingleton<IAppUserStore>(sp => new wa.infrastructure.Persistence.AppUserStore(sp.GetRequiredService<WaDbContext>()));
        // T-008-04 (TA-9.1): magic-link token store — thin adapter over the adapter-owned WaDbContext (single-use, hashed token).
        services.AddSingleton<IAuthTokenStore>(sp => new wa.infrastructure.Persistence.AuthTokenStore(sp.GetRequiredService<WaDbContext>()));
        services.AddSingleton<ISessionTokenMinter>(sp => new SessionTokenService(
            System.Text.Encoding.UTF8.GetBytes(configuration["Wa:Jwt:Secret"]!), sp.GetRequiredService<TimeProvider>().GetUtcNow));
        // T-006-03 (TA-6.7): unsubscribe token verification + suppression insert — BCL-only HMAC, same Wa:Jwt:Secret key bytes.
        services.AddSingleton<wa.infrastructure.Persistence.UnsubscribeService>(sp => new wa.infrastructure.Persistence.UnsubscribeService(
            sp.GetRequiredService<WaDbContext>(),
            System.Text.Encoding.UTF8.GetBytes(configuration["Wa:Jwt:Secret"]!),
            sp.GetRequiredService<ILogger<wa.infrastructure.Persistence.UnsubscribeService>>(), sp.GetRequiredService<TimeProvider>()));
        // US-019-03 (D-3): webhook dedup + mirror store — factory-based (fresh context per call; concurrent deliveries never share a context).
        services.AddSingleton<wa.application.UseCases.Billing.IStripeBillingStore>(sp => new wa.infrastructure.Persistence.StripeBillingStore(
            sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        // US-019-04 (D-5): payment-failed email sequence ledger (StripeEvent + JobRun rows, no migration).
        services.AddSingleton<wa.application.UseCases.Billing.IBillingSequenceStore>(sp => new wa.infrastructure.Persistence.BillingSequenceStore(
            sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
        // US-019-01 (D-1): Stripe Checkout over plain HttpClient (no Stripe package). 10 s mirrors TA-4.1.7's external-call timeout.
        services.AddSingleton(sp => new wa.infrastructure.Billing.StripeCheckoutGateway(
            new HttpClient { BaseAddress = new Uri("https://api.stripe.com/"), Timeout = TimeSpan.FromSeconds(10) },
            configuration["Wa:Stripe:SecretKey"],
            sp.GetRequiredService<ILogger<wa.infrastructure.Billing.StripeCheckoutGateway>>()));
        services.AddSingleton<wa.application.UseCases.Billing.IStripeCheckoutGateway>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());
        // US-019-07 (EC-019-4): webhook re-fetch of the subscription — same adapter, same key.
        services.AddSingleton<wa.application.UseCases.Billing.IStripeSubscriptionSource>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());

        return services;
    }
}
