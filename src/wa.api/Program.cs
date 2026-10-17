using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Health;
using wa.application.UseCases.Sample;
using wa.application.UseCases.Transfers;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
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
// T-001-05 (TA-4.2#1): limits resolution + draft SAS minting — every read through the caches (FR-053-4); guest plan resolves via seeded rows, never literals.
builder.Services.AddSingleton(sp => new WaDbContext(sp.GetRequiredService<DbContextOptions<WaDbContext>>())); // adapter-owned context; no request scope in the current API surface (same style as L29 above).
var limitsTtl = TimeSpan.FromSeconds(int.TryParse(builder.Configuration["Wa:Flags:TtlSeconds"], out var ttlSecs) ? ttlSecs : 30); // TA-3.4 — both caches share this value (Caches doc comment).
builder.Services.AddSingleton<IPlansStore>(sp => new PlansStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton<IFlagsStore>(sp => new FlagsStore(sp.GetRequiredService<WaDbContext>()));
// T-001-10 (TA-4.2#2): finalize handler's DB surface — thin adapter over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ITransferStore>(sp => new wa.infrastructure.Persistence.TransferStore(sp.GetRequiredService<WaDbContext>()));
// T-002-04 (TA-4.2#3): send handler's DB surface — thin adapter over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.ISendStore>(sp => new wa.infrastructure.Persistence.SendStore(sp.GetRequiredService<WaDbContext>()));
// T-014 (TA-4.2#4): recipient-page handler's DB surface — thin adapter over the adapter-owned WaDbContext.
builder.Services.AddSingleton<wa.application.UseCases.Transfers.IPublicTransferStore>(sp => new wa.infrastructure.Persistence.PublicTransferStore(sp.GetRequiredService<WaDbContext>()));
// T-002-02 (TA-4.1.5): generic HTTP idempotency store — used by IdempotencyMiddleware for finalize + send endpoints.
builder.Services.AddSingleton<IIdempotencyStore>(sp => new wa.infrastructure.Persistence.IdempotencyStore(sp.GetRequiredService<WaDbContext>()));
builder.Services.AddSingleton(sp => new PlansCache(sp.GetRequiredService<IPlansStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
builder.Services.AddSingleton(sp => new FlagsCache(sp.GetRequiredService<IFlagsStore>(), limitsTtl, () => DateTimeOffset.UtcNow));
builder.Services.AddSingleton<ILimitsProvider>(sp => new LimitsProvider(sp.GetRequiredService<PlansCache>(), sp.GetRequiredService<FlagsCache>()));
// TA-3.6 — minting is pure HMAC (no network at request time); §5.2 account values already read above (T-055-02).
builder.Services.AddSingleton<IBlobSasMinter>(new DraftBlobSasMinter(new Uri(blobAccountUrl!), blobAccountKey!));
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
var app = builder.Build();
// TA-4.1.6 (T-056-03): allow-list CORS only — configured origins via Wa:Cors:AllowedOrigins, no wildcard, preflight cached 1 h.
app.UseMiddleware<CorsAllowListMiddleware>();
app.UseMiddleware<CorrelationMiddleware>(); // FR-056-1: runs before everything else — correlationId present in every log line incl. UseSerilogRequestLogging's per-request INF line
// Rate limiting sits after correlation so the 429 body gets a correlationId, and BEFORE GlobalError so its empty-body 404 rewrite can't touch the 429 (the limiter short-circuits before routing).
app.UseIpRateLimiting();
// T-002-02 (TA-4.1.5): generic idempotency — before GlobalError so replays bypass error handling; on miss the full pipeline runs and the response is buffered.
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<GlobalErrorMiddleware>(); // FR-056-3: every failure → TA-4.1.3 Problem+JSON (business codes keep their status, others 500 INTERNAL)
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
        // Reason is logged by the handler (T-050-04). Body is TA-4.1.3 Problem+JSON via the shared writer; code list is closed — no DB_DOWN, so INTERNAL.
        // 503 is an explicit-status override: ProblemCodes pins INTERNAL at 500, but component-unreachable answers ServiceUnavailable (HealthFailureTests' contract).
        return Results.Json(ProblemWriter.Build(503, ProblemCodes.Internal, "A required component is unreachable."), statusCode: 503);
    }
});
app.MapPost("/api/v1/transfers/draft", async (CreateDraftCommand cmd, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(cmd, ct);
    if (result is null)
        // The handler owns the size logic (TA-4.2a thin endpoint); 422 TRANSFER_SIZE_EXCEEDED via the closed list — GlobalErrorMiddleware already maps BusinessException → ProblemWriter bodies.
        throw new BusinessException(ProblemCodes.TransferSizeExceeded, "Selection exceeds plan limits.");
    return Results.Json(new { draftId = result.DraftId.ToString("n"), expiresInSec = result.ExpiresInSec, files = result.Files.Select(f => new { fileId = f.FileId.ToString("n"), uploadUrl = f.UploadUrl.ToString() }) }, statusCode: 201); // TA-4.2#1 exact wire shape — property names pinned at the single construction site (no global JSON policy in this app).
});

// T-001-10 (TA-4.2#2): idempotent finalize — server-side blob copy staging→transfers, create Transfer(Draft)+FileItems+BlobRefs, EC-002-1 replay.
app.MapPost("/api/v1/transfers/draft/{draftId}/finalize", async (Guid draftId, HttpRequest request, ISender sender, CancellationToken ct) =>
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

    var result = await sender.Send(new FinalizeTransferCommand(draftId, idempotencyKey, files), ct);
    if (result is null)
        throw new BusinessException(ProblemCodes.TransferNotFinalized, "Draft has no completed files.");
    return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, publicUrl = result.PublicUrl, status = "Draft", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, files = result.Files.Select(f => new { id = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes, contentType = f.ContentType }) }, statusCode: 201);
});

// T-002-04 (TA-4.2#3): activate a Draft transfer — store recipients, emit transfer.created via outbox.
app.MapPost("/api/v1/transfers/{id}/send", async (Guid id, HttpRequest request, ISender sender, CancellationToken ct) =>
{
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

    try
    {
        var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password), ct);
        if (result is null)
            throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
        return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, status = "Active", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, fileCount = result.FileCount, recipients = result.Recipients }, statusCode: 201);
    }
    catch (wa.application.UseCases.Transfers.TransferValidationException vex)
    {
        throw new BusinessException(ProblemCodes.Validation, vex.Message);
    }
});

// T-014 (TA-4.2#4): recipient metadata for GET /api/v1/public/transfers/{linkId} — no auth, no cookie; page views never mint SAS or consume a download.
app.MapGet("/api/v1/public/transfers/{linkId}", async (string linkId, ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new GetPublicTransferQuery(linkId), ct);
    return result.State switch
    {
        PublicTransferState.Active => Results.Json(new
        {
            from = result.From, note = result.Note,
            files = result.Files!.Select(f => new { fileId = f.FileId.ToString("n"), name = f.Name, sizeBytes = f.SizeBytes }),
            hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired
        }), // TA-4.2#4 exact wire shape — property names pinned at the single construction site (same style as endpoints 1–3)

        // D8: status pin = 410 for both terminal expired shapes; the unknown case overrides to 404 below (FR-003-9: same body, different status line only).
        PublicTransferState.Expired => Results.Json(ProblemWriter.Build(
            ProblemCodes.StatusOf(ProblemCodes.TransferExpired), ProblemCodes.TransferExpired, ExpiredDetails(result.SenderEmail)), statusCode: 410),

        PublicTransferState.DownloadLimit => Results.Json(
            ProblemWriter.Build(410, ProblemCodes.MaxDownloadsReached, "All downloads have been used."), statusCode: 410), // 409→410 explicit override (task doc status mapping)

        PublicTransferState.NotFound => Results.Json(
            ProblemWriter.Build(404, ProblemCodes.TransferExpired, ExpiredDetails(null)), statusCode: 404) // FR-003-9: same body, different status line only
    };
});
// FR-003-8/9 details text — the single string slot in the TA-4.1.3 Problem+JSON closed shape (the writer's signature has no per-field override).
static string ExpiredDetails(string? senderEmail) =>
    senderEmail is null
        ? "This transfer has expired. Contact the sender for a new link."   // US-003-05 alternative flow: no fabricated address
        : $"This transfer has expired. Contact {senderEmail} for a new link.";

app.Run();

// Empty partial class so WebApplicationFactory<Program> can resolve the entry point.
public partial class Program { }
