using AspNetCoreRateLimit;
using Azure.Storage;
using Azure.Storage.Blobs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
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
var app = builder.Build();
// TA-4.1.6 (T-056-03): allow-list CORS only — configured origins via Wa:Cors:AllowedOrigins, no wildcard, preflight cached 1 h.
app.UseMiddleware<CorsAllowListMiddleware>();
app.UseMiddleware<CorrelationMiddleware>(); // FR-056-1: runs before everything else — correlationId present in every log line incl. UseSerilogRequestLogging's per-request INF line
// Rate limiting sits after correlation so the 429 body gets a correlationId, and BEFORE GlobalError so its empty-body 404 rewrite can't touch the 429 (the limiter short-circuits before routing).
app.UseIpRateLimiting();
// T-008-02 (TA-4.1.5): session middleware — after idempotency, before GlobalError so refreshed cookies + error bodies keep correlationId.
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<SessionMiddleware>(); // TA-9.1: reads wa.session cookie → sets HttpContext.Items['wa.session.userId']; refreshes < 15 d remaining (FR-008-2)
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
        throw new BusinessException(ProblemCodes.Validation, ex.Message);
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

    try
    {
        var result = await sender.Send(new SendTransferCommand(id, senderName, senderEmail, note, emails, password, ownerAppUserId), ct);
        if (result is null)
            throw new BusinessException(ProblemCodes.NotFound, "Transfer not found.");
        return Results.Json(new { id = result.TransferId.ToString("n"), linkId = result.LinkId, status = "Active", expiresAt = result.ExpiresAtUtc, totalBytes = result.TotalBytes, fileCount = result.FileCount, recipients = result.Recipients }, statusCode: 201);
    }
    catch (TransferSizeExceededException ex)
    {
        // T-007-04 / AC-007-1: send-time size re-check violation — same closed-code mapping as the draft endpoint (TA-4.2a).
        throw new BusinessException(ProblemCodes.TransferSizeExceeded, ex.Message);
    }
    catch (wa.application.UseCases.Transfers.TransferValidationException vex)
    {
        throw new BusinessException(ProblemCodes.Validation, vex.Message);
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
            hasDownloadAll = result.HasDownloadAll, downloadsLeft = result.DownloadsLeft, passwordRequired = result.PasswordRequired
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
        DownloadAllState.Unauthenticated => Results.Json(ProblemWriter.Build(401, ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated)), statusCode: 401)
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

// T-006-03 (FR-006-5, US-011-04): admin suppression list — F-TRF-011 owns the UI; this is the API surface.
app.MapGet("/api/v1/admin/suppressions", async (WaDbContext db, CancellationToken ct) =>
{
    var rows = await db.Set<EmailSuppression>().OrderByDescending(s => s.CreatedAtUtc).ToListAsync(ct);
    return Results.Json(rows.Select(s => new { id = s.Id.ToString("n"), address = s.Address, senderEmail = s.SenderEmail, createdAtUtc = s.CreatedAtUtc }));
});

// T-006-03 (FR-006-5, US-011-04): admin suppression remove — emits admin_action telemetry. F-TRF-011 owns the UI; this is the API surface.
app.MapDelete("/api/v1/admin/suppressions/{id:guid}", async (Guid id, WaDbContext db, wa.application.Telemetry.WaTelemetryReporter telemetry, CancellationToken ct) =>
{
    var row = await db.Set<EmailSuppression>().FirstOrDefaultAsync(s => s.Id == id, ct);
    if (row is null)
        return Results.NotFound(ProblemWriter.Build(404, ProblemCodes.NotFound, "Not found."));

    db.Set<EmailSuppression>().Remove(row);
    await db.SaveChangesAsync(ct);
    telemetry.LogEvent(TelemetryEvents.AdminAction); // F-TRF-011: admin removal is auditable via App Insights
    return Results.Ok();
});

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
        // TA-4.2a single-construction-site rule (signup/login lines ~392–401 verbatim): 'n'-form userId, exact key set.
        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode
        }, statusCode: 200);
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
        var result = await sender.Send(new UpdateProfileCommand(userId, req.DisplayName, req.Theme), ct);
        return Results.Json(new
        {
            userId = result.UserId.ToString("n"),
            email = result.Email,
            displayName = result.DisplayName,
            theme = result.Theme,
            planCode = result.PlanCode
        }, statusCode: 200);
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
        throw new BusinessException(ProblemCodes.Validation, ex.Message, ex);
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
    // Resolved byte set threaded into the query — null = no status predicate (all), {1} = active, {2,3} = expired.
    static IReadOnlyCollection<byte>? ResolveStatusFilter(HttpRequest request)
    {
        var raw = request.Query["status"].FirstOrDefault();
        if (raw is null) return null;
        return raw.Trim().ToLowerInvariant() switch
        {
            "all" => null,
            "active" => new byte[] { 1 },
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
            recipientCount = r.RecipientCount
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

app.Run();

// Empty partial class so WebApplicationFactory<Program> can resolve the entry point.
public partial class Program { }
