using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.application.UseCases.Features;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain; // T-006-03: EmailSuppression entity type for admin routes
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.domain.Telemetry; // T-006-03: admin_action event name for suppression removal
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;
using wa.api.Middleware;
using Serilog;
using Serilog.Context;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
// TA-10.5: console sink locally (App Insights later per environment); Microsoft.* and AspNetCoreRateLimit.* → Warning (TA-9.4: the limiter's blocked-request INF line carries the raw client IP), app at Information.
builder.Host.UseSerilog((context, cfg) => cfg
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("AspNetCoreRateLimit", LogEventLevel.Warning)
    .WriteTo.Console());
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PingCommand).Assembly));
builder.Services.AddSingleton<wa.application.Ports.IDbHealthProbe, WaDbHealthProbe>();
builder.Services.AddSingleton<wa.application.Ports.ISbHealthProbe, SbHealthProbe>();
// Adapter-owned contexts (no request scope in the current API surface).
builder.Services.AddSingleton(new DbContextOptionsBuilder<WaDbContext>()
    .UseSqlServer(builder.Configuration.GetConnectionString("WaDb")).Options);
builder.Services.AddSingleton<wa.infrastructure.Persistence.AppDbContextFactory>();
// SbEventPublisher (T-054-03) resolves the interface — register under both types.
builder.Services.AddSingleton<IDbContextFactory<WaDbContext>, wa.infrastructure.Persistence.AppDbContextFactory>();
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
builder.Services.AddSingleton(sp => new AzureBlobStore(
    new Uri(blobAccountUrl!), blobAccountKey!, sp.GetRequiredService<ILogger<AzureBlobStore>>()));
builder.Services.AddSingleton<wa.application.Ports.IBlobStore>(sp => sp.GetRequiredService<AzureBlobStore>());
builder.Services.AddSingleton<wa.application.Ports.IBlobContentWriter>(sp => sp.GetRequiredService<AzureBlobStore>()); // US-021-04: logo upload with Content-Type
// BlobContainerBootstrap (TA-3.5) needs a raw client for service-level properties; account name via the adapter's helper.
builder.Services.AddSingleton(new BlobServiceClient(
    new Uri(blobAccountUrl!),
    new StorageSharedKeyCredential(AzureBlobStore.AccountNameFrom(new Uri(blobAccountUrl!)), blobAccountKey!)));
builder.Services.AddHostedService<BlobContainerBootstrap>();
// T-001-05 (TA-4.2#1): limits resolution + draft SAS minting — every read through the caches (FR-053-4); guest plan resolves via seeded rows, never literals.
builder.Services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>())); // adapter-owned context; no request scope in the current API surface (same style as L29 above).
var limitsTtl = TimeSpan.FromSeconds(int.TryParse(builder.Configuration["Wa:Flags:TtlSeconds"], out var ttlSecs) ? ttlSecs : 30); // TA-3.4 — both caches share this value (Caches doc comment).
builder.Services.AddSingleton<IPlansStore>(sp => new PlansStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton<IFlagsStore>(sp => new FlagsStore(sp.GetRequiredService<WaDbContext>()));
// T-001-10 (TA-4.2#2): finalize handler's DB surface — thin adapter over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ITransferStore>(sp => new wa.infrastructure.Persistence.TransferStore(sp.GetRequiredService<WaDbContext>()));
// T-002-04 (TA-4.2#3): send handler's DB surface — thin adapter over the adapter-owned WaDbContext.
// T-010-02: IBlobStore (the gate primitive, registered as a singleton at L53) — SendStore's FILES_GONE gate checks physical presence via ExistsAsync.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ISendStore>(sp => new wa.infrastructure.Persistence.SendStore(sp.GetRequiredService<WaDbContext>(), sp.GetRequiredService<wa.application.Ports.IBlobStore>()));
// T-014 (TA-4.2#4): recipient-page handler's DB surface — thin adapter over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.IPublicTransferStore>(sp => new wa.infrastructure.Persistence.PublicTransferStore(sp.GetRequiredService<WaDbContext>()));
// US-021-01: sender branding read port (org name beside the sender on the recipient page).
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ISenderBrandingStore>(sp => new wa.infrastructure.Persistence.SenderBrandingStore(sp.GetRequiredService<WaDbContext>()));
// US-021-04: org logo path write port (DELETE /auth/me/logo).
builder.Services.AddSingleton<wa.application.UseCases.Auth.IOrgLogoStore>(sp => new wa.infrastructure.Persistence.OrgLogoStore(sp.GetRequiredService<WaDbContext>()));
// F-PRF-001: org name write port (PATCH /auth/me orgName).
builder.Services.AddSingleton<wa.application.UseCases.Auth.IOrgNameStore>(sp => new wa.infrastructure.Persistence.OrgNameStore(sp.GetRequiredService<WaDbContext>()));
// T-002-02 (TA-4.1.5): generic HTTP idempotency store — used by IdempotencyMiddleware for finalize + send endpoints.
builder.Services.AddSingleton<IIdempotencyStore>(sp => new wa.infrastructure.Persistence.IdempotencyStore(sp.GetRequiredService<WaDbContext>()));
// T-011-02 (FR-011-5 / TA-3.2): audit write helper — one AuditLog row + one admin.action domain event per mutation.
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAuditStore>(sp => new wa.infrastructure.Persistence.AuditStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAuditWriter>(sp => new wa.application.UseCases.Admin.AuditWriter(sp.GetRequiredService<wa.application.UseCases.Admin.IAuditStore>(), sp.GetRequiredService<wa.application.Ports.IEventPublisher>()));
builder.Services.AddSingleton(sp => new PlansCache(sp.GetRequiredService<IPlansStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
builder.Services.AddSingleton(sp => new FlagsCache(sp.GetRequiredService<IFlagsStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
// T-011-03 (TA-4.2#20 / FR-011-6): overview aggregates — store + 5-min snapshot cache (pinned by spec; not config-driven).
builder.Services.AddSingleton<wa.application.UseCases.Admin.IOverviewStore>(sp => new wa.infrastructure.Persistence.OverviewStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton(sp => new wa.application.UseCases.Admin.OverviewSnapshotCache(sp.GetRequiredService<wa.application.UseCases.Admin.IOverviewStore>(), TimeSpan.FromMinutes(5), () => DateTimeOffset.UtcNow));
// T-011-04 (TA-4.2#21 / FR-011-3): admin transfer search — store over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Admin.ISearchTransfersStore>(sp => new wa.infrastructure.Persistence.SearchTransfersStore(sp.GetRequiredService<WaDbContext>()));
// T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — store over the adapter-owned WaDbContext (platform-scoped predicate).
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAdminTransferStore>(sp => new wa.infrastructure.Persistence.AdminTransferStore(sp.GetRequiredService<WaDbContext>()));
// T-011-06 (TA-4.2#22 / FR-011-2): admin users list — store over the adapter-owned WaDbContext (computed plan/storage/count).
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAdminUserStore>(sp => new wa.infrastructure.Persistence.AdminUserStore(sp.GetRequiredService<WaDbContext>()));
// T-011-08 (TA-4.2#23 / FR-011-2): admin flag write surface — single-key PK upsert over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAdminFlagStore>(sp => new wa.infrastructure.Persistence.AdminFlagStore(sp.GetRequiredService<WaDbContext>()));
// T-011-09 (TA-4.2#24 / FR-011-2): admin suppressions surface — list + remove over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAdminSuppressionStore>(sp => new wa.infrastructure.Persistence.AdminSuppressionStore(sp.GetRequiredService<WaDbContext>()));
// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): audit read surface — last count rows, newest first, over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Admin.IAuditReadStore>(sp => new wa.infrastructure.Persistence.AuditReadStore(sp.GetRequiredService<WaDbContext>()));
// US-047-03 (FR-047-6): erasure report read — fresh context per call (DataExportStore precedent).
builder.Services.AddSingleton<wa.application.UseCases.Admin.IErasureReportStore>(sp => new wa.infrastructure.Persistence.ErasureReportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
builder.Services.AddSingleton(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
builder.Services.AddSingleton<ILimitsProvider>(sp => sp.GetRequiredService<LimitsProvider>());
builder.Services.AddSingleton<IFlagResolver>(sp => sp.GetRequiredService<LimitsProvider>()); // US-044-02: phase gates (FR-044-3)
// T-031-05 (US-018-05, TA-3.4): feature toggles — one resolver over the same caches.
builder.Services.AddSingleton<IFeaturesProvider>(sp => new FeaturesProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
// US-020-01 (FR-020-1): per-request PlanContext — user→plan-code port (singleton) + scoped accessor bound by PlanContextMiddleware.
builder.Services.AddSingleton<IUserPlanStore>(sp => new wa.infrastructure.Persistence.UserPlanStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddScoped<PlanContextAccessor>();
builder.Services.AddScoped<IPlanContextAccessor>(sp => sp.GetRequiredService<PlanContextAccessor>());
// TA-3.6 — minting is pure HMAC (no network at request time); §5.2 account values already read above (T-055-02).
var sasMinter = new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!);
builder.Services.AddSingleton<IBlobSasMinter>(sasMinter);
builder.Services.AddSingleton<IExportSasMinter>(sasMinter); // US-047-01: export download SAS (TA-3.6)
builder.Services.AddSingleton<IOrgLogoSasMinter>(sasMinter); // US-021-04: org logo read SAS (TA-3.6)
builder.Services.AddSingleton<wa.application.UseCases.DataExport.IDataExportStore>(sp => new wa.infrastructure.Persistence.DataExportStore(sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
// TA-9.5 second line (T-056-03): AspNetCoreRateLimit frozen at 5.0.0, in-memory store — per-instance state is correct at M0 scale; a distributed store would be an ADR change.
builder.Services.AddMemoryCache(); // the package's MemoryCache* stores need IMemoryCache, which this app does not register by default (DI validation caught it)
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>(); // v5.0.0 leaves this to the caller — AsyncKeyLockProcessingStrategy requires it and AddInMemoryRateLimiting registers no implementation
builder.Services.AddInMemoryRateLimiting(); // must run before Build() — the middleware pulls RateLimitConfiguration from DI
// FR-056-3 (T-056-03): *:/api/v1/auth/* = 5/min per IP → 429 RATE_LIMITED + Retry-After, body via ProblemWriter. The rule's Endpoint keeps the literal `*:` prefix —
// the processor matches GeneralRules against both `*:{path}` and `{verb}:{path}`, so a plain path would never align (Core/RateLimitProcessor.cs).
var authRule = new RateLimitRule { Endpoint = "*:/api/v1/auth/*", Period = "1m", Limit = 5 };
// On breach the library's ReturnQuotaExceededResponse writes AFTER our hook on the same unstarted response — an empty application/json body keeps the Problem payload intact.
authRule.QuotaExceededResponse = new QuotaExceededResponse { StatusCode = 429, ContentType = "application/json", Content = "" };
builder.Services.Configure<IpRateLimitOptions>(opts =>
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
builder.Services.AddSingleton<wa.application.Telemetry.WaTelemetryReporter>(sp => new wa.application.Telemetry.WaTelemetryReporter(
    sp.GetRequiredService<ILogger<wa.application.Telemetry.WaTelemetryReporter>>(),
    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Wa:Jwt:Secret"]!))); // TA-9.4: the IP HMAC key IS Wa:Jwt:Secret — UTF-8 of the raw string (same key T-008's session cookie signs with, TA-9.1)
// T-003-04 (TA-4.2#5): unlock-JWT minter — BCL-only HS256, the SAME Wa:Jwt:Secret bytes (TA-9.1: "same key T-008's session cookie signs with").
// Injectable clock arg is () => DateTimeOffset.UtcNow in production; unit tests pin exp via their own instance (D3).
builder.Services.AddSingleton<IUnlockTokenMinter>(sp => new UnlockTokenService(
    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Wa:Jwt:Secret"]!), () => DateTimeOffset.UtcNow));
// T-008-01 (TA-4.2#8): signup's DB surface — thin adapter over the adapter-owned WaDbContext, and its TA-9.1 session-token minter (30-day rolling).
builder.Services.AddSingleton<IAppUserStore>(sp => new wa.infrastructure.Persistence.AppUserStore(sp.GetRequiredService<WaDbContext>()));
// T-008-04 (TA-9.1): magic-link token store — thin adapter over the adapter-owned WaDbContext (single-use, hashed token).
builder.Services.AddSingleton<IAuthTokenStore>(sp => new wa.infrastructure.Persistence.AuthTokenStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton<ISessionTokenMinter>(sp => new SessionTokenService(
    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Wa:Jwt:Secret"]!), () => DateTimeOffset.UtcNow));
// T-006-03 (TA-6.7): unsubscribe token verification + suppression insert — BCL-only HMAC, same Wa:Jwt:Secret key bytes.
builder.Services.AddSingleton<wa.infrastructure.Persistence.UnsubscribeService>(sp => new wa.infrastructure.Persistence.UnsubscribeService(
    sp.GetRequiredService<WaDbContext>(),
    System.Text.Encoding.UTF8.GetBytes(builder.Configuration["Wa:Jwt:Secret"]!),
    sp.GetRequiredService<ILogger<wa.infrastructure.Persistence.UnsubscribeService>>()));
// US-019-03 (D-3): webhook dedup + mirror store — factory-based (fresh context per call; concurrent deliveries never share a context).
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeBillingStore>(sp => new wa.infrastructure.Persistence.StripeBillingStore(
    sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
// US-019-04 (D-5): payment-failed email sequence ledger (StripeEvent + JobRun rows, no migration).
builder.Services.AddSingleton<wa.application.UseCases.Billing.IBillingSequenceStore>(sp => new wa.infrastructure.Persistence.BillingSequenceStore(
    sp.GetRequiredService<IDbContextFactory<WaDbContext>>()));
// US-019-01 (D-1): Stripe Checkout over plain HttpClient (no Stripe package). 10 s mirrors TA-4.1.7's external-call timeout.
builder.Services.AddSingleton(sp => new wa.infrastructure.Billing.StripeCheckoutGateway(
    new HttpClient { BaseAddress = new Uri("https://api.stripe.com/"), Timeout = TimeSpan.FromSeconds(10) },
    builder.Configuration["Wa:Stripe:SecretKey"],
    sp.GetRequiredService<ILogger<wa.infrastructure.Billing.StripeCheckoutGateway>>()));
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeCheckoutGateway>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());
// US-019-07 (EC-019-4): webhook re-fetch of the subscription — same adapter, same key.
builder.Services.AddSingleton<wa.application.UseCases.Billing.IStripeSubscriptionSource>(sp => sp.GetRequiredService<wa.infrastructure.Billing.StripeCheckoutGateway>());
var app = builder.Build();

// T-011-01 (TA-9.2 / FR-011-1): role is derived PER REQUEST from the two config lists — same cookie, role flips when a list changes (EC-011-3).
// Wa:AdminEmails = frozen key (D-18 dev seed "dev@example.com"); Wa:OperatorEmails = NEW optional key — absent/empty → every admin is SuperAdmin.
var adminEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var operatorEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var part in builder.Configuration["Wa:AdminEmails"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
    adminEmails.Add(part);
foreach (var part in builder.Configuration["Wa:OperatorEmails"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
    operatorEmails.Add(part);

// T-011-01 gate helper — the contract endpoints 20–23 call as their routes land (T-011-03…T-011-09). Session read → 401 UNAUTHENTICATED inline
// (same idiom as /auth/me L578); unknown email OR not in Wa:AdminEmails → FORBIDDEN; SuperAdmin-only route with an Operator role → same FORBIDDEN.
// Returns the role (US-047-03 email reveal). No audit row for a rejected gate — the suppression DELETE emits TelemetryEvents.AdminAction AFTER the gate passes (T-006-03).
async Task<AdminRole> RequireAdminAsync(HttpContext ctx, IAppUserStore users, bool superadminOnly, CancellationToken ct)
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        throw new BusinessException(ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated));

    var user = await users.GetByIdAsync(userId, ct);
    var role = user?.Email is { } email ? AdminRolePolicy.Resolve(email.ToLowerInvariant(), adminEmails, operatorEmails) : null;
    if (role is null || (superadminOnly && role != AdminRole.SuperAdmin))
        throw new BusinessException(ProblemCodes.Forbidden, ProblemCodes.TitleOf(ProblemCodes.Forbidden));
    return role.Value;
}

// TA-4.1.6 (T-056-03): allow-list CORS only — configured origins via Wa:Cors:AllowedOrigins, no wildcard, preflight cached 1 h.
app.UseMiddleware<CorsAllowListMiddleware>();
app.UseMiddleware<CorrelationMiddleware>(); // FR-056-1: runs before everything else — correlationId present in every log line incl. UseSerilogRequestLogging's per-request INF line
// Rate limiting sits after correlation so the 429 body gets a correlationId, and BEFORE GlobalError so its empty-body 404 rewrite can't touch the 429 (the limiter short-circuits before routing).
app.UseIpRateLimiting();
// T-008-02 (TA-4.1.5): session middleware — after idempotency, before GlobalError so refreshed cookies + error bodies keep correlationId.
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<SessionMiddleware>(); // TA-9.1: reads wa.session cookie → sets HttpContext.Items['wa.session.userId']; refreshes < 15 d remaining (FR-008-2)
app.UseMiddleware<GlobalErrorMiddleware>(); // FR-056-3: every failure → TA-4.1.3 Problem+JSON (business codes keep their status, others 500 INTERNAL)
app.UseMiddleware<PlanContextMiddleware>(); // FR-020-1 / US-020-01: binds session user → PlanContext (guest → Free); resolution happens on first read
app.UseSerilogRequestLogging(options => options.GetMessageTemplateProperties = LogRoute.RequestLogProperties); // per-request INF line (Microsoft.* request logging is suppressed to Warning); TA-9.4: RequestPath = route template (/unsubscribe/{token}), never the raw path
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
        // Reason is logged by the handler (T-050-04). Body is TA-4.1.3 Problem+JSON via the shared writer; code list is closed — no DB_DOWN, so INTERNAL.
        // 503 is an explicit-status override: ProblemCodes pins INTERNAL at 500, but component-unreachable answers ServiceUnavailable (HealthFailureTests' contract).
        return Results.Json(ProblemWriter.Build(503, ProblemCodes.Internal, "A required component is unreachable."), statusCode: 503);
    }
});

// T-013-01 (FR-013-2): dev-only chaos endpoint — forces both failure shapes for E2E without prod surface.
// kind=internal → InvalidOperationException → GlobalErrorMiddleware → 500 INTERNAL Problem+JSON.
// kind=rate_limited → ProblemWriter.WriteCompleteAsync(429, RATE_LIMITED) — byte-identical to the real limiter breach path (L115).
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/v1/dev/chaos", async (HttpContext httpContext, HttpRequest request) =>
    {
        var kind = request.Query["kind"].FirstOrDefault();
        switch (kind)
        {
            case "internal":
                throw new InvalidOperationException("T-013-01 chaos: forced internal failure"); // bubbles to GlobalErrorMiddleware → 500 INTERNAL
            case "rate_limited":
                await ProblemWriter.WriteCompleteAsync(httpContext, 429, ProblemCodes.RateLimited, "Too many requests — limit is 5 per minute.");
                return; // WriteCompleteAsync already commits headers + writes the body — do NOT also return Results.Json (double-serialize)
            default:
                throw new BusinessException(ProblemCodes.Validation, "kind must be one of internal / rate_limited.");
        }
    });
}
app.MapPost("/api/v1/transfers/draft", async (CreateDraftCommand cmd, ISender sender, CancellationToken ct) =>
{
    CreateDraftResult result;
    try
    {
        result = await sender.Send(cmd, ct);
    }
    catch (TransferSizeExceededException ex)
    {
        // The handler owns the size logic (TA-4.2a thin endpoint); 422 TRANSFER_SIZE_EXCEEDED via the closed list — GlobalErrorMiddleware already maps BusinessException → ProblemWriter bodies.
        throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
    }

    return Results.Json(new { draftId = result.DraftId.ToString("n"), expiresInSec = result.ExpiresInSec, files = result.Files.Select(f => new { fileId = f.FileId.ToString("n"), uploadUrl = f.UploadUrl.ToString() }) }, statusCode: 201); // TA-4.2#1 exact wire shape — property names pinned at the single construction site (no global JSON policy in this app).
});

// T-001-10 (TA-4.2#2): idempotent finalize — server-side blob copy staging→transfers, create Transfer(Draft)+FileItems+BlobRefs, EC-002-1 replay.
app.MapPost("/api/v1/transfers/draft/{draftId}/finalize", async (Guid draftId, HttpContext httpContext, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    ms.Position = 0;
    using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
    var files = bodyDoc.RootElement.GetProperty("files").EnumerateArray().Select(e => new FinalizeFile
    {
        FileId = Guid.Parse(e.GetProperty("fileId").GetString()!),
        Name = e.GetProperty("name").GetString()!,
        SizeBytes = e.GetProperty("sizeBytes").GetInt64(),
        ContentType = e.TryGetProperty("contentType", out var ct) ? ct.GetString() : null,
        Retries = e.TryGetProperty("retries", out var r) ? r.GetInt32() : 0
    }).ToList();

    // T-008-02 (TA-9.1): read owner AppUserId from the session middleware's Items (single parsing site — SessionMiddleware).
    // Missing/invalid/expired → guest (item null); same wire behavior as before, no inline HMAC decode.
    Guid? ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

    try
    {
        var result = await sender.Send(new FinalizeTransferCommand(draftId, idempotencyKey, files, ownerAppUserId), ct);
        if (result is null)
            throw new BusinessException(ProblemCodes.TransferNotFinalized, "Draft has no completed files.");
        return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, publicUrl = result.PublicUrl, status = "Draft", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, files = result.Files.Select(f => new { id = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes, contentType = f.ContentType }) }, statusCode: 201);
    }
    catch (StorageQuotaExceededException ex) when (ex is not null)
    {
        throw new BusinessException(ProblemCodes.StorageQuotaExceeded, ex.Message);
    }
    catch (ActiveTransfersExceededException ex) when (ex is not null)
    {
        // US-020-02 (FR-020-2): dedicated closed code, was VALIDATION.
        throw new BusinessException(ProblemCodes.ActiveTransfersExceeded, ex.Message);
    }
    catch (BlobSizeMismatchException ex) when (ex is not null)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message);
    }
});

// T-002-04 (TA-4.2#3): activate a Draft transfer — store recipients, emit transfer.created via outbox.
app.MapPost("/api/v1/transfers/{id}/send", async (Guid id, HttpContext httpContext, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    // T-008-08 (FR-008-7): owner AppUserId from the session middleware's Items — single parsing site (T-008-02). Missing/invalid/expired → guest (null).
    var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    ms.Position = 0;
    using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
    var root = bodyDoc.RootElement;
    var emails = root.TryGetProperty("emails", out var e) ? e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList() : (IReadOnlyList<string>)[];
    var senderName = root.TryGetProperty("senderName", out var sn) ? sn.GetString() : null;
    var senderEmail = root.TryGetProperty("senderEmail", out var se) ? se.GetString() : null;
    var note = root.TryGetProperty("note", out var n) ? n.GetString() : null;
    var password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null;

    // US-022-01 (D-3): optional scheduledSendAt — ISO-8601 with offset; absent/null = send now; anything else → 400 VALIDATION.
    DateTimeOffset? scheduledSendAt = null;
    if (root.TryGetProperty("scheduledSendAt", out var sa) && sa.ValueKind != System.Text.Json.JsonValueKind.Null)
    {
        if (sa.ValueKind != System.Text.Json.JsonValueKind.String || !sa.TryGetDateTimeOffset(out var parsedSchedule))
            throw new BusinessException(ProblemCodes.Validation, "scheduledSendAt must be an ISO 8601 date-time with an offset.");
        scheduledSendAt = parsedSchedule;
    }

    try
    {
        var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password, ownerAppUserId, scheduledSendAt?.UtcDateTime), ct);
        if (result is null)
            throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
        return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, status = result.Status == wa.domain.TransferStatus.Scheduled ? "Scheduled" : "Active", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, fileCount = result.FileCount, recipients = result.Recipients, scheduledSendAt = result.ScheduledSendAtUtc }, statusCode: 201);
    }
    catch (wa.application.UseCases.Transfers.ScheduleForbiddenException ex)
    {
        // US-022-01 (FR-022-3): plan/gate does not allow scheduling → 403 FORBIDDEN (closed list unchanged).
        throw new BusinessException(ProblemCodes.Forbidden, ex.Message);
    }
    catch (wa.application.UseCases.Transfers.ScheduleOutOfWindowException ex)
    {
        // US-022-03 (FR-022-2): scheduledSendAt outside now+5 min .. now+14 days (server time) → 422 SCHEDULE_OUT_OF_WINDOW.
        throw new BusinessException(ProblemCodes.ScheduleOutOfWindow, ex.Message);
    }
    catch (TransferSizeExceededException ex)
    {
        // T-007-04 / AC-007-1: send-time size re-check violation — same closed-code mapping as the draft endpoint (TA-4.2a).
        throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
    }
    catch (ActiveTransfersExceededException ex)
    {
        // US-020-02 (FR-020-2): send-time active-transfers re-check (downgraded / at-cap account).
        throw new BusinessException(ProblemCodes.ActiveTransfersExceeded, ex.Message);
    }
    catch (wa.application.UseCases.Transfers.TransferValidationException vex)
    {
        throw new BusinessException(ProblemCodes.Validation, vex.Message);
    }
    catch (wa.application.UseCases.Transfers.FilesGoneException ex)
    {
        // T-010-02 (EC-010-2): a re-send draft's blobs are physically gone → 410 FILES_GONE.
        throw new BusinessException(ProblemCodes.FilesGone, ex.Message);
    }
});

// T-010-01 (endpoint 16): re-send — create a new Draft transfer sharing the original's blobs (no data copy, ADR-009).
// Cookie required; no request body. Guest row → 403 FORBIDDEN (EC-010-4); foreign owner / unknown id / Status=4 → 404 NOT_FOUND.
app.MapPost("/api/v1/transfers/{id}/resend", async (Guid id, HttpContext httpContext, ISender sender, CancellationToken ct) =>
{
    // T-008-08 (FR-008-7): owner AppUserId from the session middleware's Items — single parsing site (T-008-02). Missing/invalid/expired → guest (null).
    var ownerAppUserId = httpContext.Items[SessionMiddleware.SessionUserIdKey] as Guid?;

    try
    {
        var result = await sender.Send(new wa.application.UseCases.Transfers.ResendTransferCommand(id, ownerAppUserId), ct);
        if (result is null)
            throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
        return Results.Json(new
        {
            id = result.TransferId.ToString("n"),
            linkId = result.LinkId,
            status = "Draft",
            expiresAt = result.ExpiresAtUtc,
            totalBytes = result.TotalBytes,
            fileCount = result.FileCount,
            files = result.Files.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name }),
            recipients = result.Recipients,
            note = result.Note,
            senderName = result.SenderName,
            senderEmail = result.SenderEmail,
            passwordRequired = result.PasswordRequired
        }, statusCode: 201);
    }
    catch (wa.application.UseCases.Transfers.ResendForbiddenException ex)
    {
        // EC-010-4: a guest row (no owner) is re-send-ineligible → 403 FORBIDDEN — first usage of ProblemCodes.Forbidden.
        throw new BusinessException(ProblemCodes.Forbidden, ex.Message);
    }
    catch (wa.application.UseCases.Transfers.FilesGoneException ex)
    {
        // T-010-02 (AC-010-2): the original's blobs are physically gone → 410 FILES_GONE — zero new rows, no RefCount change.
        throw new BusinessException(ProblemCodes.FilesGone, ex.Message);
    }
});

// T-014 (TA-4.2#4): recipient metadata for GET /api/v1/public/transfers/{linkId} — no auth, no cookie; page views never mint SAS or consume a download.
app.MapGet("/api/v1/public/transfers/{linkId}", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    // T-003-04 (TA-9.6#1): the ?t= unlock token is read here and passed to the handler — a valid token flips passwordRequired to false.
    var result = await sender.Send(new GetPublicTransferQuery(linkId, request.Query["t"].FirstOrDefault()), ct);
    return result.State switch
    {
        PublicTransferState.Active => Results.Json(new
        {
            from = result.From, note = result.Note,
            files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
            hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired,
            orgName = result.OrgName, orgLogoUrl = result.OrgLogoUrl,
            scheduledSendAt = result.ScheduledSendAtUtc is { } due ? AsUtc(due) : (DateTime?)null
        }), // TA-4.2#4 exact wire shape — property names pinned at the single construction site (same style as endpoints 1–3)

        // D8: status pin = 410 for both terminal expired shapes; the unknown case overrides to 404 below (FR-003-9: same body, different status line only).
        PublicTransferState.Expired => Results.Json(ProblemWriter.Build(
            ProblemCodes.StatusOf(ProblemCodes.TransferExpired), ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),

        PublicTransferState.DownloadLimit => Results.Json(
            ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410), // 409→410 explicit override (task doc status mapping)

        PublicTransferState.NotFound => Results.Json(
            ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404) // FR-003-9: same body, different status line only
    };
});
// FR-003-8/9 details text — the single string slot in the TA-4.1.3 Problem+JSON closed shape (the writer's signature has no per-field override).
static string ExpiredDetails(string? senderEmail) =>
    senderEmail is null
        ? "This transfer has expired. Contact the sender for a new link."   // US-003-05 alternative flow: no fabricated address
        : $"This transfer has expired. Contact {senderEmail} for a new link.";

// AC-003-5 / F-TRF-005-4 details text — sender email when provided; plain message when null (no fabricated address, same style as ExpiredDetails).
static string DownloadLimitDetails(string? senderEmail) =>
    senderEmail is null
        ? "All downloads have been used."
        : $"All downloads have been used. Contact {senderEmail} for a new link.";

// T-003-04 (TA-4.2#5): unlock a gated transfer — PBKDF2-SHA256 verify (same hasher + user-id keying as send time) → 7-day HS256 JWT.
// No Set-Cookie, no extra properties (T-003-05: the token lives client-side in sessionStorage); endpoint 4's ?t= accepts it.
app.MapPost("/api/v1/public/transfers/{linkId}/unlock", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    // Same JsonDocument style as the send endpoint: empty body → password stays null → the handler throws the VALIDATION application-level exception (D1).
    string? password = null;
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    if (ms.Length > 0)
    {
        ms.Position = 0;
        using var bodyDoc = System.Text.Json.JsonDocument.Parse(ms);
        password = bodyDoc.RootElement.TryGetProperty("password", out var pw) ? pw.GetString() : null;
    }

    try
    {
        var result = await sender.Send(new UnlockTransferCommand(linkId, password), ct);
        return result.State switch
        {
            // TA-4.2#5 exact wire shape — { token } only.
            UnlockState.Active => Results.Json(new { token = result.Token }),
            // D2.6: the auth failure is a result-state (not an exception) — Program.cs owns the 401 body; title pinned by the closed table.
            UnlockState.WrongPassword => Results.Json(ProblemWriter.Build(401, ProblemCodes.WrongPassword, ProblemCodes.TitleOf(ProblemCodes.WrongPassword)), statusCode: 401),
            // FR-003-9 single-body invariant — byte-equal to endpoint 4's unknown-linkId case (status line is the only diff).
            UnlockState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
            // D6: status-before-password — terminal rows map to their canonical bodies regardless of credentials.
            UnlockState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
            UnlockState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410)
        };
    }
    catch (wa.application.UseCases.Transfers.UnlockPasswordMissingException vex)
    {
        // D1: application-level exception → VALIDATION with errors[] — the only code with per-field details; BusinessException stays in wa.api.Middleware.
        throw new BusinessException(ProblemCodes.Validation, vex.Message) { Errors = [new ProblemFieldError("password", vex.Message)] };
    }
});

// T-003-06 (TA-4.2#6): mint a 30-minute read SAS on one transfer file; the browser streams directly from storage.
// Terminal states map to endpoint 4's canonical Problem+JSON bodies (FR-003-9 byte identity); ?t= unlock JWT accepted.
app.MapGet("/api/v1/public/transfers/{linkId}/files/{fileId}/download-url", async (string linkId, string fileId, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new GetFileDownloadUrlQuery(
        linkId,
        fileId,
        Token: request.Query["t"].FirstOrDefault(),
        ClientIp: request.HttpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent: request.Headers.UserAgent.ToString()), ct);
    return result.State switch
    {
        // TA-4.2#6 exact wire shape — exactly { url, expiresAt } (02-feature-plan §F-TRF-003 pinned); nothing else.
        FileDownloadState.Ok => Results.Json(new { url = result.Url!.ToString(), expiresAt = result.ExpiresAt!.Value }),

        // D6: terminal-before-auth — canonical bodies, same as endpoint 4 (FR-003-9).
        FileDownloadState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
        FileDownloadState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
        FileDownloadState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410),

        // TA-9.6#1: gated transfer minted without a valid ?t= → 401 UNAUTHENTICATED (never a file-list-shaped error).
        FileDownloadState.Unauthenticated => Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401)
    };
});

// T-004-02 (TA-4.2#7): mint a 30-minute read SAS on the whole-transfer zip if it exists, otherwise trigger f-zip generation
// (fire-and-forget) and return 202 with pollAfterSec. Terminal states map to endpoint 4's canonical Problem+JSON bodies (FR-003-9 byte identity); ?t= unlock JWT accepted.
app.MapGet("/api/v1/public/transfers/{linkId}/download-all-url", async (string linkId, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new GetDownloadAllUrlQuery(
        linkId,
        Token: request.Query["t"].FirstOrDefault(),
        ClientIp: request.HttpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent: request.Headers.UserAgent.ToString()), ct);
    return result.State switch
    {
        // TA-4.2#7 exact wire shape — exactly { url } when the zip is ready (200).
        DownloadAllState.Ok => Results.Json(new { url = result.Url!.ToString() }),

        // TA-4.2#7 exact wire shape — exactly { pollAfterSec } when the zip is being generated (202).
        DownloadAllState.Generating => Results.Json(new { pollAfterSec = result.PollAfterSec!.Value }, statusCode: 202),

        // D6: terminal-before-auth — canonical bodies, same as endpoint 4 (FR-003-9).
        DownloadAllState.NotFound => Results.Json(ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404),
        DownloadAllState.Expired => Results.Json(ProblemWriter.Build(410, ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),
        DownloadAllState.DownloadLimit => Results.Json(ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, DownloadLimitDetails(result.SenderEmail)), statusCode: 410),

        // TA-9.6#1: gated transfer minted without a valid ?t= → 401 UNAUTHENTICATED (never a file-list-shaped error).
        DownloadAllState.Unauthenticated => Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401),

        // T-013-03 (FR-013-3): blob account down at mint time — 503 + code INTERNAL (no new closed-list code; TA-4.1.3's INTERNAL row).
        DownloadAllState.StorageUnavailable => Results.Json(ProblemWriter.Build(503, ProblemCodes.Internal, "A required component is unreachable."), statusCode: 503)
    };
});

// T-006-03 (FR-006-5): public unsubscribe — GET /unsubscribe/{token}. Token = Base64Url(payload).Base64Url(HMACSHA256(payload, Wa:Jwt:Secret)).
// Payload = "address|senderEmail". On success: insert EmailSuppression (idempotent via UQ_ES) + 200. Tampered/unknown → 404 + warn telemetry.
app.MapGet("/unsubscribe/{token}", async (string token, wa.infrastructure.Persistence.UnsubscribeService unsubscribe, CancellationToken ct) =>
{
    var result = await unsubscribe.HandleAsync(token, ct);
    return result.Success
        ? Results.Ok() // 200 — the SPA shows the success card + redirects to /
        : Results.NotFound(ProblemWriter.Build(404, ProblemCodes.NotFound, "Not found.")); // 404 — don't leak detail (TA-9.6)
});

// T-011-09 (TA-4.2#24 / FR-011-2): admin suppression list — thin endpoint over MediatR (was inline EF from T-006-03).
app.MapGet("/api/v1/admin/suppressions", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles — every GET of 20–23 passes Operator + SuperAdmin (AC-011-3 server half).

    var rows = await sender.Send(new GetSuppressionsQuery(), ct);
    return Results.Json(rows.Select(s => new { id = s.Id.ToString("n"), address = s.Address, senderEmail = s.SenderEmail, createdAtUtc = s.CreatedAtUtc })); // bare array — UnsubscribeEndpointTests L145 pins the wire shape
});

// T-011-09 (TA-4.2#24 / FR-011-5): admin suppression remove — thin endpoint over MediatR; audits every removal + emits admin_action telemetry.
app.MapDelete("/api/v1/admin/suppressions/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, wa.application.Telemetry.WaTelemetryReporter telemetry, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: true, ct); // T-011-01: suppression remove is in the exact SuperAdmin-only write set (AC-011-3 server half).

    // T-011-09: null result = unknown id — ONE case (endpoint maps to 404 NOT_FOUND via BusinessException; mirrors T-011-05/07).
    var result = await sender.Send(new RemoveSuppressionCommand(id), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.NotFound, "Not found.");

    telemetry.LogEvent(TelemetryEvents.AdminAction); // F-TRF-011: admin removal is auditable via App Insights (T-006-03 FR-006-5 surface — kept)

    // FR-011-5: audit only on successful removal — re-read actor email via IAppUserStore (the gate returns void).
    // The removed row's address comes from the handler RESULT (captured PRE-delete) — the row is gone and unreadable now.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user?.Email is not null)
            await auditWriter.WriteAsync(user.Email, "suppression.removed", "emailSuppression", id.ToString("n"), new { email = result.Address }, ct);
    }

    // KEEP 200 OK — UnsubscribeEndpointTests L180 asserts OK (NOT NoContent like T-011-05/07).
    return Results.Ok();
});

// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): admin recent actions — last 50 rows, newest first; both roles read.
app.MapGet("/api/v1/admin/audit-recent", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L459.

    var rows = await sender.Send(new GetRecentAuditQuery(), ct);
    return Results.Json(rows.Select(a => new { id = a.Id, actorEmail = a.ActorEmail, action = a.Action, entityType = a.EntityType, entityId = a.EntityId, detailsJson = a.DetailsJson, createdAtUtc = a.CreatedAtUtc })); // bare array, camelCase — new route (no existing wire shape)
});

// T-011-03 (TA-4.2#20 / FR-011-6): admin overview — four aggregates + recent transfers list, cached 5 min in-memory.
app.MapGet("/api/v1/admin/overview", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L450.

    // T-013-05 (AC-013-3): DLQ count is broker-side — ServiceBusAdministrationClient from Wa:ServiceBus:ConnectionString (same key as SbEventPublisher/SbHealthProbe).
    // Defensive: a dummy/unreachable connection (dev/test) must never 5xx the overview — FR-013-4 in spirit.
    long dlqCount = 0;
    var sbConnString = builder.Configuration["Wa:ServiceBus:ConnectionString"];
    if (!string.IsNullOrEmpty(sbConnString))
    {
        try
        {
            var adminClient = new Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient(sbConnString);
            var response = await adminClient.GetSubscriptionRuntimePropertiesAsync("core", "email");
            dlqCount = response.Value.DeadLetterMessageCount;
        }
        catch (Exception)
        {
            // Broker unreachable or subscription missing — leave 0; the overview must not be blocked by SB being down.
        }
    }

    var result = await sender.Send(new wa.application.UseCases.Admin.GetOverviewQuery(), ct);
    return Results.Json(new
    {
        activeTransfers = result.ActiveTransfers,
        storageBytesUsed = result.StorageBytesUsed,
        newUsersToday = result.NewUsersToday,
        emailsSent7d = result.EmailsSent7d,
        emailsFailed7d = result.EmailsFailed7d,
        emailPendingCount = result.EmailPendingCount,
        emailFailedTotal = result.EmailFailedTotal,
        lastSuccessfulDeliveryAge = result.LastSuccessfulDeliveryAge,
        dlqCount,
        snapshotAtUtc = result.SnapshotAtUtc,
        recentTransfers = result.RecentTransfers.Select(r => new { linkId = r.LinkId, ownerEmail = r.OwnerEmail, status = r.Status, totalBytes = r.TotalBytes, createdAtUtc = r.CreatedAtUtc })
    });
});

// T-011-04 (TA-4.2#21 / FR-011-3): admin transfer search — GET /api/v1/admin/transfers?q=&status=&cursor=&minBytes=&maxBytes=&from=&to=&limit=.
// Cursor-paged at 25 rows (TA-4.1.4); thin endpoint gated by T-011-01's RequireAdminAsync (superadminOnly:false — both roles read).
app.MapGet("/api/v1/admin/transfers", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L475.

    // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
    var limitParam = request.Query["limit"].FirstOrDefault();
    if (limitParam is null)
        limitParam = "25";
    if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
        throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

    // T-011-04: optional filters — q/status/minBytes/maxBytes/from/to (all optional; absent → no predicate).
    var q = request.Query["q"].FirstOrDefault();
    var status = request.Query["status"].FirstOrDefault();
    long? minBytes = ParseLongParam(request, "minBytes");
    long? maxBytes = ParseLongParam(request, "maxBytes");
    DateTime? fromUtc = ParseDateTimeParam(request, "from");
    DateTime? toUtc = ParseDateTimeParam(request, "to");

    string? cursor = request.Query["cursor"].FirstOrDefault();

    try
    {
        var result = await sender.Send(new wa.application.UseCases.Admin.SearchTransfersQuery(
            q, status, minBytes, maxBytes, fromUtc, toUtc, cursor, parsedLimit), ct);
        return Results.Json(new
        {
            items = result.Items.Select(r => new
            {
                id = r.Id,
                linkId = r.LinkId,
                ownerEmail = r.OwnerEmail,
                status = r.Status,
                fileCount = r.FileCount,
                totalBytes = r.TotalBytes,
                createdAtUtc = r.CreatedAtUtc,
                downloadsCount = r.DownloadsCount
            }),
            nextCursor = result.NextCursor
        });
    }
    catch (wa.domain.Transfers.CursorFormatException ex)
    {
        // TA-4.1.4: malformed cursor → 400 VALIDATION (existing closed-list member) — same as Program.cs L715-723.
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
    catch (wa.application.UseCases.Admin.SearchTransfersValidationException ex)
    {
        // T-011-04: handler validation (status vocabulary / range pairing) → 400 VALIDATION (existing closed-list member).
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-011-06 (TA-4.2#22 / FR-011-2): admin users list — GET /api/v1/admin/users?q=&cursor=&limit=.
// Cursor-paged at 25 rows (TA-4.1.4); thin endpoint gated by T-011-01's RequireAdminAsync (superadminOnly:false — both roles read).
app.MapGet("/api/v1/admin/users", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct); // T-011-01: both roles read — same as the GET at L498.

    // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
    var limitParam = request.Query["limit"].FirstOrDefault();
    if (limitParam is null)
        limitParam = "25";
    if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
        throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

    // T-011-06: optional filters — q (email substring) + cursor (all optional; absent → no predicate).
    var q = request.Query["q"].FirstOrDefault();
    string? cursor = request.Query["cursor"].FirstOrDefault();

    try
    {
        var result = await sender.Send(new wa.application.UseCases.Admin.GetUsersQuery(q, cursor, parsedLimit), ct);
        return Results.Json(new
        {
            items = result.Items.Select(r => new
            {
                id = r.Id,
                email = r.Email,
                planName = r.PlanName,
                storageBytesUsed = r.StorageBytesUsed,
                activeTransferCount = r.ActiveTransferCount,
                createdAtUtc = r.CreatedAtUtc
            }),
            nextCursor = result.NextCursor
        });
    }
    catch (wa.domain.Transfers.CursorFormatException ex)
    {
        // TA-4.1.4: malformed cursor → 400 VALIDATION (existing closed-list member) — same as Program.cs L538-542.
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// US-047-03 (FR-047-6, TA-4.1.4): erasure report — both roles read; email revealed to SuperAdmin (DPO) only, masked for Operator (TA-9.4).
app.MapGet("/api/v1/admin/erasures", async (HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    var role = await RequireAdminAsync(ctx, users, superadminOnly: false, ct);
    var pageSize = wa.application.UseCases.Admin.ErasureReportPolicy.PageSize;
    var limitParam = request.Query["limit"].FirstOrDefault() ?? pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!int.TryParse(limitParam, out var limit) || limit <= 0 || limit > pageSize)
        throw new BusinessException(ProblemCodes.Validation, $"limit must be a positive integer at most {pageSize}.");
    try
    {
        var result = await sender.Send(new wa.application.UseCases.Admin.GetErasuresQuery(
            request.Query["cursor"].FirstOrDefault(), limit, RevealEmail: role == AdminRole.SuperAdmin), ct);
        return Results.Json(new
        {
            items = result.Items.Select(r => new
            {
                userId = r.UserId.ToString("n"),
                email = r.Email,
                deletedAtUtc = AsUtc(r.DeletedAtUtc),
                reHomedTransferCount = r.ReHomedTransferCount,
                sweepStatus = r.SweepStatus,
                sweptAtUtc = r.SweptAtUtc is { } swept ? AsUtc(swept) : (DateTime?)null
            }),
            nextCursor = result.NextCursor
        });
    }
    catch (wa.domain.Transfers.CursorFormatException ex)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — DELETE /api/v1/admin/transfers/{id} — SuperAdmin-only write.
// Reuses T-009-04's delete semantics verbatim with reason "admin" — row marked Deleted (soft, Status=4), transfer.deleted
// emitted once { transferId, reason:"admin" }, RefCount decremented per BlobRef, sole-ref blob gets PhysicallyDeletedAtUtc = now+24h.
app.MapDelete("/api/v1/admin/transfers/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
{
    // T-011-01 gate: SuperAdmin-only write (TA-4.2#21). The gate resolves the role internally and returns nothing —
    // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
    await RequireAdminAsync(ctx, users, superadminOnly: true, ct);

    // T-011-05: null result = already-deleted / unknown — ONE indistinguishable case (EC-009-2 idempotency reused).
    var result = await sender.Send(new AdminDeleteTransferCommand(id), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

    // FR-011-5: audit only on successful delete — re-read actor email via IAppUserStore (the gate returns void).
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user?.Email is not null)
            await auditWriter.WriteAsync(user.Email, "transfer.force_delete", "transfer", id.ToString("n"), new { reason = "admin", transferId = id }, ct);
    }

    // FR-011-4: 204 No Content — no body (same as endpoint 17 at Program.cs L883).
    return Results.NoContent();
});

// T-011-07 (TA-4.2#22 / FR-011-2): set a user's plan — PATCH /api/v1/admin/users/{id}?planId= — SuperAdmin-only write.
// Limits take effect at the NEXT finalize (FR-BIL-001-4 "applies to new transfers") — no limit recompute here.
app.MapPatch("/api/v1/admin/users/{id:guid}", async (Guid id, HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
{
    // T-011-01 gate: SuperAdmin-only write (TA-4.2#22). The gate resolves the role internally and returns nothing —
    // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
    await RequireAdminAsync(ctx, users, superadminOnly: true, ct);

    // T-011-07: planId from the query string — bad format → 400 VALIDATION (existing closed-list member).
    var planIdParam = request.Query["planId"].FirstOrDefault();
    if (!Guid.TryParse(planIdParam, out var planId))
        throw new BusinessException(ProblemCodes.Validation, "planId must be a GUID.");

    try
    {
        var result = await sender.Send(new SetUserPlanCommand(id, planId), ct);

        // FR-011-5: audit only on successful write — re-read actor email via IAppUserStore (the gate returns void).
        if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user?.Email is not null)
                await auditWriter.WriteAsync(user.Email, "user.plan_changed", "user", id.ToString("n"), new { oldPlanId = result.OldPlanId, newPlanId = result.NewPlanId }, ct);
        }

        // FR-011-2: 204 No Content — no body (same as endpoint 21 at Program.cs L616).
        return Results.NoContent();
    }
    catch (SetUserPlanException ex)
    {
        // T-011-07 (Decision 2): unknown user OR unknown planId → 404 NOT_FOUND (existing closed-list member).
        throw new BusinessException(ProblemCodes.NotFound, ex.Message, ex);
    }
});

// T-011-07 (TA-4.2#22 / FR-011-2): force-delete a user — DELETE /api/v1/admin/users/{id} — SuperAdmin-only write.
// Reuses T-008's GDPR soft-delete + re-home verbatim (reason "admin" is the admin context, not a column).
app.MapDelete("/api/v1/admin/users/{id:guid}", async (Guid id, HttpContext ctx, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
{
    // T-011-01 gate: SuperAdmin-only write (TA-4.2#22). The gate resolves the role internally and returns nothing —
    // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
    await RequireAdminAsync(ctx, users, superadminOnly: true, ct);

    // T-011-07: null result = unknown user OR already soft-deleted — ONE indistinguishable case (EC-009-2 idempotency reused).
    var result = await sender.Send(new AdminForceDeleteUserCommand(id), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.NotFound, "User not found.");

    // FR-011-5: audit only on successful delete — re-read actor email via IAppUserStore (the gate returns void).
    // The target user's email comes from the result (captured PRE-delete) — the row is soft-deleted and unreadable now.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user?.Email is not null)
            await auditWriter.WriteAsync(user.Email, "user.force_delete", "user", id.ToString("n"), new { email = result.Email }, ct);
    }

    // FR-011-2: 204 No Content — no body (same as endpoint 21 at Program.cs L616).
    return Results.NoContent();
});

// T-011-08 (TA-4.2#23 / FR-011-2): admin flags list — GET /api/v1/admin/flags — both roles read.
// 52 fixed seeded rows — no paging; bare JSON array of {key,value}, camelCase, ordered by key ascending.
app.MapGet("/api/v1/admin/flags", async (HttpContext ctx, IAppUserStore users, ISender sender, CancellationToken ct) =>
{
    // T-011-01 gate: both roles read — same as the GET at L556.
    await RequireAdminAsync(ctx, users, superadminOnly: false, ct);

    var rows = await sender.Send(new GetFlagsQuery(), ct);
    // US-044-01: effective-value view — type, plan default and overridden marker drive the Flags screen Limits group.
    return Results.Json(rows.Select(f => new { key = f.Key, value = f.Value, updatedAt = f.UpdatedAtUtc, type = f.Type, planDefault = f.PlanDefault, overridden = f.Overridden, description = f.Description })); // reads write NO audit row
});

// T-011-08 (TA-4.2#23 / FR-011-2): set a flag's value — PUT /api/v1/admin/flags/{key} — SuperAdmin-only write.
// The value takes effect within 30 s via the EXISTING FlagsCache TTL (TA-3.4) — no invalidation code here.
app.MapPut("/api/v1/admin/flags/{key}", async (string key, HttpContext ctx, HttpRequest request, IAppUserStore users, ISender sender, IAuditWriter auditWriter, CancellationToken ct) =>
{
    // T-011-01 gate: SuperAdmin-only write (TA-4.2#23). The gate resolves the role internally and returns nothing —
    // 401 UNAUTHENTICATED / 403 FORBIDDEN via BusinessException (GlobalErrorMiddleware maps them).
    await RequireAdminAsync(ctx, users, superadminOnly: true, ct);

    // T-011-08: the raw body string is the new value — passed through to the handler (TA-4.2a thin endpoint).
    var body = await new StreamReader(request.Body).ReadToEndAsync(ct);

    try
    {
        var result = await sender.Send(new SetFlagCommand(key, body), ct);

        // FR-011-5: audit only on successful save — re-read actor email via IAppUserStore (the gate returns void).
        if (ctx.Items[SessionMiddleware.SessionUserIdKey] is Guid userId)
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user?.Email is not null)
                await auditWriter.WriteAsync(user.Email, "flag.set", "flag", key, new { oldValue = result.OldValue, newValue = result.NewValue }, ct);
        }

        // US-044-01 (FR-044-5): 200 with the new value ({key,value}).
        return Results.Json(new { key = result.Key, value = result.NewValue });
    }
    catch (SetFlagException ex)
    {
        // US-044-01 (AC-044-3): unknown key → 404 FLAG_UNKNOWN (checked first); invalid typed value → 400 FLAG_INVALID.
        throw new BusinessException(ex.Code, ex.Message, ex);
    }
});

// T-011-04: helper — parse an optional long query param; bad int → 400 VALIDATION.
static long? ParseLongParam(HttpRequest request, string name)
{
    var raw = request.Query[name].FirstOrDefault();
    if (raw is null || raw.Length == 0) return null;
    if (!long.TryParse(raw, out var value))
        throw new BusinessException(ProblemCodes.Validation, $"{name} must be an integer.");
    return value;
}

// T-011-04: helper — parse an optional DateTime query param (RoundtripKind); bad date → 400 VALIDATION.
static DateTime? ParseDateTimeParam(HttpRequest request, string name)
{
    var raw = request.Query[name].FirstOrDefault();
    if (raw is null || raw.Length == 0) return null;
    if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value))
        throw new BusinessException(ProblemCodes.Validation, $"{name} must be a valid date/time.");
    return value;
}

// T-008-01 (TA-4.2#8 / FR-008-3): sign-up is sign-in — unknown email → create; known email (any case) → existing row.
app.MapPost("/api/v1/auth/signup", async (SignupCommand cmd, ISender sender, CancellationToken ct) =>
{
    try
    {
        var result = await sender.Send(cmd, ct);
        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode,
            sessionToken = result.SessionToken
        }, statusCode: 200);
    }
    catch (SignupValidationException ex)
    {
        // T-056 pipeline maps BusinessException → Problem+JSON; no new code — reuse VALIDATION (TA-4.1.3).
        // Preserve the exception chain: the inner keeps its stack trace (Serilog), wire shape still comes from Code + the pipeline.
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-008-04 (FR-008-1 / TA-9.1): POST /api/v1/auth/magic-link — request a sign-in link. 202 Accepted with no body.
app.MapPost("/api/v1/auth/magic-link", async (RequestMagicLinkCommand cmd, ISender sender, CancellationToken ct) =>
{
    try
    {
        await sender.Send(cmd, ct);
        return Results.Accepted(); // 202 — link is in-flight; body carries nothing (TA-4.2#10)
    }
    catch (RequestMagicLinkValidationException ex)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-008-05 (FR-008-6 / TA-4.2#11): POST /api/v1/auth/forgot-password — request a sign-in link with the forgot-password email.
// Anti-enumeration: 202 Accepted, NO body, byte-identical for registered AND unregistered (D-5). Body: { email }.
app.MapPost("/api/v1/auth/forgot-password", async (ForgotPasswordCommand cmd, ISender sender, wa.application.Telemetry.WaTelemetryReporter telemetry, CancellationToken ct) =>
{
    try
    {
        await sender.Send(cmd, ct);
        telemetry.ForgotPasswordRequested(); // D-4: request-volume metric — only on the 202 path (a 400 isn't an abuse signal)
        return Results.Accepted(); // 202 — identical for registered/unregistered (anti-enumeration; TA-4.2#11)
    }
    catch (ForgotPasswordValidationException ex)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-008-04 (FR-008-1 / FR-008-3 / AC-008-2 / EC-008-3): POST /api/v1/auth/magic-link/redeem — sign in via magic link.
// Body: { token }. Success → 200 + same wire shape as login + wa.session cookie (TA-9.1).
// LinkExpiredException (expired OR already redeemed) → 410 LINK_EXPIRED (identical state, don't leak which — EC-008-3).
// Invalid token → also 410 LINK_EXPIRED (AC-008-2: second click invalid; don't leak "token not found" vs "expired").
app.MapPost("/api/v1/auth/magic-link/redeem", async (RedeemMagicLinkCommand cmd, ISender sender, HttpContext httpContext, CancellationToken ct) =>
{
    try
    {
        var result = await sender.Send(cmd, ct);

        // TA-9.1: set wa.session cookie from the minted token (cookie value == body's sessionToken; httpOnly, SameSite=Lax).
        httpContext.Response.Cookies.Append("wa.session", result.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(SessionTokenService.LifetimeDays) // frozen 30 d — no magic number
        });

        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode,
            sessionToken = result.SessionToken
        }, statusCode: 200);
    }
    catch (RedeemValidationException ex)
    {
        // TA-4.1.3: malformed/missing token → VALIDATION (400), NOT LINK_EXPIRED (plan §ERROR CASES).
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
    catch (LinkExpiredException)
    {
        // EC-008-3 + AC-008-2: expired ≡ redeemed ≡ unknown token — all 410 LINK_EXPIRED. No leakage.
        return Results.Json(ProblemWriter.Build(410, ProblemCodes.LinkExpired, ProblemCodes.TitleOf(ProblemCodes.LinkExpired)), statusCode: 410);
    }
});

// T-008-02 (TA-4.2#9 / FR-008-1): login — signs in an existing user. Same wire shape as signup + sets wa.session cookie (TA-9.1).
app.MapPost("/api/v1/auth/login", async (LoginCommand cmd, ISender sender, HttpContext httpContext, CancellationToken ct) =>
{
    try
    {
        var result = await sender.Send(cmd, ct);

        // TA-9.1: set wa.session cookie from the minted token (cookie value == body's sessionToken; httpOnly, SameSite=Lax).
        httpContext.Response.Cookies.Append("wa.session", result.SessionToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // TA-9.1 + FR-008-2 (not asserted in local plain-HTTP tests — no prior test does)
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(SessionTokenService.LifetimeDays) // frozen 30 d — no magic number
        });

        // TA-4.2a: exact wire shape as signup (userId 'n' form, email, displayName, theme, planCode, sessionToken).
        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode,
            sessionToken = result.SessionToken
        }, statusCode: 200);
    }
    catch (LoginWrongPasswordException ex)
    {
        // T-056 pipeline maps BusinessException → Problem+JSON; 401 WRONG_PASSWORD via the closed list (no new code).
        throw new BusinessException(ProblemCodes.WrongPassword, ex.Message, ex);
    }
    catch (LoginValidationException ex)
    {
        // T-056 pipeline maps BusinessException → Problem+JSON; no new code — reuse VALIDATION (TA-4.1.3).
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
});

// T-008-06 (FR-008-4 / TA-4.2#13): GET /api/v1/auth/me — the session user's profile DTO.
// Wire shape = login's minus sessionToken (the cookie already carries the session; no re-mint).
app.MapGet("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var result = await sender.Send(new GetProfileCommand(userId), ct);
        // T-011-01 (TA-9.2 / UI-Reference §5.6): additive role field — "superadmin" or "operator"; ABSENT for non-admin so the UI renders disabled buttons with tooltip, not hidden elements.
        var roleField = AdminRolePolicy.RoleField(AdminRolePolicy.Resolve(result.Email.ToLowerInvariant(), adminEmails, operatorEmails));
        // TA-4.2a exact key set (signup/login verbatim order). Dictionary preserves insertion order — the additive 'role' key is present ONLY when non-null, so a non-admin's body carries no role key at all (not "role":null).
        var meBody = new Dictionary<string, object>
        {
            ["userId"] = result.UserId.ToString("n"),
            ["email"] = result.Email,
            ["displayName"] = result.DisplayName,
            ["theme"] = result.Theme,
            ["planCode"] = result.PlanCode,
            ["orgName"] = result.OrgName! // F-PRF-001 (FR-021-1): null when unset; the profile page reads it
        };
        if (roleField is not null)
            meBody["role"] = roleField;
        return Results.Json(meBody, statusCode: 200);
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        // Valid cookie but no AppUser row (unknown OR soft-deleted) → 404 NOT_FOUND (spec's literal pin).
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
});

// T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial update (only provided fields change).
app.MapPatch("/api/v1/auth/me", async (UpdateProfileRequest req, HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        // Single construction site (TA-4.2a): UserId from the session cookie — NOT in the JSON body.
        var result = await sender.Send(new UpdateProfileCommand(userId, req.DisplayName, req.Theme, req.OrgName), ct);
        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode,
            orgName = result.OrgName
        }, statusCode: 200);
    }
    catch (OrgLogoRejectedException ex)
    {
        // Free plan setting an org name (branding is Pro) -> same 403 FORBIDDEN as the logo upload.
        throw new BusinessException(ex.Code, ex.Message, ex);
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        // Same as GET — valid cookie, no row → 404 NOT_FOUND.
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
    catch (MeProfileException ex)
    {
        // Validation family (invalid theme / too-long name / empty PATCH) → BusinessException(Validation, ...) —
        // inner-exception-preservation pattern from T-008-01 fix round.
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex)
        {
            Errors = ex.Field is null ? null : [new ProblemFieldError(ex.Field, ex.Message)]
        };
    }
});

// T-008-06 (TA-4.2#12): POST /api/v1/auth/logout — clear the session cookie. No use case: cookie clearing is
// endpoint-level (the middleware owns parsing — same as T-008-02). No telemetry beyond metrics (task spec §3).
app.MapPost("/api/v1/auth/logout", (HttpContext ctx) =>
{
    ctx.Response.Cookies.Delete("wa.session"); // the middleware only refreshes when a token is present; deleting leaves the next request as guest
    return Results.Ok(); // 200 no body — top bar returns to "Sign in" via the SPA's redirect
});

// T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's My Files history list, cursor-paged at 25 rows (TA-4.1.4).
app.MapGet("/api/v1/transfers", async (HttpContext ctx, HttpRequest request, ISender sender, CancellationToken ct) =>
{
    // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    var limitParam = request.Query["limit"].FirstOrDefault();
    // TA-4.1.4 pin: default page size 25; reject limit <= 0 or > 25 with 400 VALIDATION (existing closed-list member).
    if (limitParam is null)
        limitParam = "25";
    if (!int.TryParse(limitParam, out var parsedLimit) || parsedLimit is <= 0 or > 25)
        throw new BusinessException(ProblemCodes.Validation, "limit must be a positive integer at most 25.");

    string? cursor = request.Query["cursor"].FirstOrDefault();
    // TA-4.1.4: decode base64("{createdAtUtc:O}:{id:N}") early so a malformed cursor → 400 VALIDATION (existing closed-list member).
    if (!string.IsNullOrEmpty(cursor))
        try
        {
            _ = wa.domain.Transfers.CursorCodec.Decode(cursor);
        }
        catch (wa.domain.Transfers.CursorFormatException ex)
        {
            throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
        }

    // T-009-02 (US-009-02): optional ?status=all|active|expired (case-insensitive; absent → all).
    // Resolved byte set threaded into the query — null = no status predicate (all), {1,5} = active (Active + Scheduled), {2,3} = expired.
    static IReadOnlyCollection<byte>? ResolveStatusFilter(HttpRequest request)
    {
        var raw = request.Query["status"].FirstOrDefault();
        if (raw is null) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "all" => null,
            "active" => new byte[] { 1, wa.domain.TransferStatus.Scheduled }, // F-PRF-002: Scheduled (5) rows are live, so "active" shows them
            "expired" => new byte[] { 2, 3 }, // US-009-02: Status IN (2,3) — download-limit included
            _ => throw new BusinessException(ProblemCodes.Validation, "status must be one of all / active / expired.")
        };
    }

    var result = await sender.Send(new ListMyTransfersQuery(userId, cursor, parsedLimit, ResolveStatusFilter(request)), ct);
    // TA-4.2a single-construction-site rule: 'n'-form id (signup/login style); opaque nextCursor as returned (null = last page).
    return Results.Json(new
    {
        items = result.Items.Select(r => new
        {
            id = r.Id.ToString("n"),
            name = r.Name,
            fileCount = r.FileCount,
            totalBytes = r.TotalBytes,
            status = r.Status,
            expiresAtUtc = r.ExpiresAtUtc,
            downloadsCount = r.DownloadsCount,
            maxDownloads = r.MaxDownloads,
            recipientCount = r.RecipientCount,
            // T-009-06 (FR-009-3): linkId — Copy/View build {origin}/t/{linkId} client-side (no per-row round-trip).
            linkId = r.LinkId
        }),
        nextCursor = result.NextCursor
    }, statusCode: 200);
});

// T-009-03 (TA-4.2#15): GET /api/v1/transfers/{id} — the session user's transfer detail: row + per-file rows + recipients.
app.MapGet("/api/v1/transfers/{id}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    // T-009-03: null result = foreign owner / guest-created / deleted / unknown — ONE indistinguishable case (no FORBIDDEN).
    var result = await sender.Send(new GetMyTransferQuery(id, userId), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

    // TA-4.2a single-construction-site rule: T-009-01's exact row wire names (UI treats list rows and detail identically)
    // PLUS the per-file rows + recipient addresses (FR-009-3 row actions copy-link / view / re-send / delete).
    return Results.Json(new
    {
        id = result.Row.Id.ToString("n"),
        name = result.Row.Name,
        fileCount = result.Row.FileCount,
        totalBytes = result.Row.TotalBytes,
        status = result.Row.Status,
        expiresAtUtc = result.Row.ExpiresAtUtc,
        downloadsCount = result.Row.DownloadsCount,
        maxDownloads = result.Row.MaxDownloads,
        recipientCount = result.Row.RecipientCount,
        // T-009-06 (FR-009-3): linkId — Copy/View build {origin}/t/{linkId} client-side (no per-row round-trip).
        linkId = result.Row.LinkId,
        files = result.Files.Select(f => new { name = f.Name, sizeBytes = f.SizeBytes }),
        recipients = result.Recipients
    }, statusCode: 200);
});

// T-009-04 (TA-4.2#17 / FR-009-3): DELETE /api/v1/transfers/{id} — owner delete, soft-delete + refcount path.
app.MapDelete("/api/v1/transfers/{id}", async (Guid id, HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    // Shared session-read idiom (T-008-02): SessionMiddleware.SessionUserIdKey as Guid? — null for guests.
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    // T-009-04: null result = foreign owner / guest-created / already-deleted / unknown — ONE indistinguishable case (EC-009-2).
    var result = await sender.Send(new DeleteTransferCommand(id, userId, "user"), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");

    // FR-009-3: 204 No Content — no body (the client treats 404 on a second delete as success per EC-009-2).
    return Results.NoContent();
});

// T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers + account_deleted once.
app.MapDelete("/api/v1/auth/me", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var result = await sender.Send(new DeleteAccountCommand(userId), ct);

        // account_deleted already went out via the outbox (EC-008-2: exactly one event for one deletion — dedup on EventId).
        ctx.Response.Cookies.Delete("wa.session"); // clear cookie exactly like logout
        return Results.Json(new { userId = userId.ToString("n") }, statusCode: 200); // body mirrors GET's shape minus profile fields
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        // Same as GET — valid cookie, no row → 404 NOT_FOUND.
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
});

// US-021-04 (FR-021-3 / AC-021-5): DELETE /api/v1/auth/me/logo — remove the session user's org logo (idempotent; not plan-gated).
app.MapDelete("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        await sender.Send(new RemoveOrgLogoCommand(userId), ct);
        return Results.NoContent();
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
});

// US-021-04 (FR-021-3..5): PUT /api/v1/auth/me/logo — raw image body; the handler bounds the read, sniffs the content and stores it.
app.MapPut("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var view = await sender.Send(new UploadOrgLogoCommand(userId, ctx.Request.Body), ct);
        return Results.Json(ToLogoBody(view), statusCode: 200);
    }
    catch (OrgLogoRejectedException ex)
    {
        throw new BusinessException(ex.Code, ex.Message, ex);
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
});

// US-021-04 (D-B5): GET /api/v1/auth/me/logo — owner view { logoUrl, maxLogoSize, brandingEnabled }.
app.MapGet("/api/v1/auth/me/logo", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var view = await sender.Send(new GetOrgLogoQuery(userId), ct);
        return Results.Json(ToLogoBody(view), statusCode: 200);
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
});

static object ToLogoBody(OrgLogoView view) =>
    new { logoUrl = view.LogoUrl?.ToString(), maxLogoSize = view.MaxLogoSize, brandingEnabled = view.BrandingEnabled };

// US-047-01 (FR-047-1/2/4): POST /api/v1/auth/me/export — request a GDPR data export (gated by feature.dataExport); 202 + processing view.
app.MapPost("/api/v1/auth/me/export", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var view = await sender.Send(new wa.application.UseCases.DataExport.ExportMyDataCommand(userId), ct);
        return Results.Json(ToExportBody(view), statusCode: 202);
    }
    catch (wa.application.UseCases.DataExport.DataExportInFlightException)
    {
        throw new BusinessException(ProblemCodes.ExportInFlight, "Your export is already preparing.");
    }
    catch (MeProfileException ex) when (ex.Message == ProblemCodes.TitleOf(ProblemCodes.NotFound))
    {
        return Results.Json(ProblemWriter.Build(404, ProblemCodes.NotFound, ProblemCodes.TitleOf(ProblemCodes.NotFound)), statusCode: 404);
    }
}).RequireFeature(FeatureGates.DataExport);

// US-047-01 (FR-047-2/4): GET /api/v1/auth/me/export — latest export state (+ fresh 24 h downloadUrl when ready). NOT gated: in-flight jobs still complete and stay downloadable.
app.MapGet("/api/v1/auth/me/export", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    var view = await sender.Send(new wa.application.UseCases.DataExport.GetDataExportQuery(userId), ct);
    return view is null ? Results.NoContent() : Results.Json(ToExportBody(view), statusCode: 200);
});

// EF reads datetime2 back as Kind=Unspecified (no trailing Z → browsers parse as local time); US-047 wire timestamps are UTC by contract.
static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

static Dictionary<string, object?> ToExportBody(wa.application.UseCases.DataExport.DataExportView view)
{
    var body = new Dictionary<string, object?> { ["state"] = view.State, ["createdAtUtc"] = AsUtc(view.CreatedAtUtc) };
    if (view.State == "ready")
    {
        body["readyAtUtc"] = view.ReadyAtUtc is { } readyAt ? AsUtc(readyAt) : null;
        body["downloadUrl"] = view.DownloadUrl?.ToString();
    }
    return body;
}

// US-019-03 (TA-4.2#19 / FR-019-5): POST /api/v1/billing/webhooks/stripe — Stripe-Signature is the auth (no session gate).
app.MapPost("/api/v1/billing/webhooks/stripe", async (HttpRequest request, ISender sender, CancellationToken ct) =>
{
    string payload;
    using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
        payload = await reader.ReadToEndAsync(ct);

    var secret = builder.Configuration["Wa:Stripe:WebhookSecret"];
    if (!wa.application.UseCases.Billing.StripeWebhookSignature.IsValid(
            request.Headers["Stripe-Signature"].ToString(), payload, secret, DateTimeOffset.UtcNow))
        throw new BusinessException(ProblemCodes.StripeWebhookMismatch, "Stripe signature verification failed.");

    try
    {
        await sender.Send(new wa.application.UseCases.Billing.HandleStripeWebhookCommand(payload), ct);
    }
    catch (wa.application.UseCases.Billing.StripeWebhookException ex) when (!ex.Retryable)
    {
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
    }
    // Retryable StripeWebhookException → GlobalErrorMiddleware → 500 INTERNAL → Stripe redelivers (EC-019-1).

    return Results.Ok();
});

// US-019-01 (TA-4.2#18 / FR-019-1): POST /api/v1/billing/checkout — cookie auth; returns the Stripe-hosted Checkout URL. Persists nothing.
app.MapPost("/api/v1/billing/checkout", async (wa.application.UseCases.Billing.CheckoutRequest req, HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    if (string.IsNullOrWhiteSpace(req?.PlanCode))
        throw new BusinessException(ProblemCodes.Validation, "planCode is required.");

    BillingInterval interval;
    if (req.Interval is null || string.Equals(req.Interval, "monthly", StringComparison.OrdinalIgnoreCase))
        interval = BillingInterval.Monthly;
    else if (string.Equals(req.Interval, "annual", StringComparison.OrdinalIgnoreCase))
        interval = BillingInterval.Annual;
    else
        throw new BusinessException(ProblemCodes.Validation, "interval must be 'monthly' or 'annual'.");

    try
    {
        var session = await sender.Send(
            new wa.application.UseCases.Billing.CreateCheckoutSessionCommand(userId, req.PlanCode.Trim(), interval), ct);
        return Results.Ok(new { url = session.Url, sessionId = session.SessionId });
    }
    catch (wa.application.UseCases.Billing.CheckoutException ex)
    {
        var code = ex.Code == wa.application.UseCases.Billing.CheckoutException.FailureCode.NotFound
            ? ProblemCodes.NotFound
            : ProblemCodes.Validation;
        throw new BusinessException(code, ex.Message, ex);
    }
    catch (wa.application.UseCases.Billing.BillingUnavailableException ex)
    {
        throw new BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex);
    }
});

// US-019-02 (TA-4.2#18): GET /api/v1/billing/portal — cookie auth; Stripe Billing Portal URL for the user's customer, or the upgrade fallback. Persists nothing.
app.MapGet("/api/v1/billing/portal", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    try
    {
        var result = await sender.Send(new wa.application.UseCases.Billing.GetPortalSessionQuery(userId), ct);
        return Results.Ok(new { url = result.Url, fallback = result.Url is null ? "upgrade" : (string?)null });
    }
    catch (wa.application.UseCases.Billing.BillingUnavailableException ex)
    {
        throw new BusinessException(ProblemCodes.BillingUnavailable, "Billing is temporarily unavailable — please try again.", ex);
    }
});

// US-020-03 (FR-020-3 / AC-020-3): GET /api/v1/billing/plan. Cookie auth. Current plan + live usage vs. PlanContext limits. Persists nothing.
app.MapGet("/api/v1/billing/plan", async (HttpContext ctx, ISender sender, CancellationToken ct) =>
{
    if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
        return Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401);

    var r = await sender.Send(new wa.application.UseCases.Billing.GetPlanUsageQuery(userId), ct);
    return Results.Ok(new
    {
        planCode = r.PlanCode,
        storage = new { usedBytes = r.StorageUsedBytes, allowedBytes = r.StorageAllowedBytes },
        activeTransfers = new { used = r.ActiveTransfersUsed, allowed = r.ActiveTransfersAllowed },
        limits = new { maxTransferSize = r.MaxTransferSize, maxSingleFile = r.MaxSingleFile },
        features = new { scheduling = r.Scheduling },   // US-022-01 (FR-022-3, D-6)
        scheduleWindow = new { minMinutes = wa.domain.Transfers.ScheduleWindow.MinMinutes, maxDays = wa.domain.Transfers.ScheduleWindow.MaxDays }   // US-022-03 (FR-022-2)
    });
});

// US-044-02 (FR-044-7, AC-044-2): the six phase-gate booleans for the SPA's FeatureGate — anonymous, no PII, no telemetry (TA-10.2).
app.MapGet("/api/v1/features", async (ISender sender, CancellationToken ct) =>
    Results.Json(await sender.Send(new GetFeatureGatesQuery(), ct)));

app.Run();

// Empty partial class so WebApplicationFactory<Program> can resolve the entry point.
public partial class Program { }
