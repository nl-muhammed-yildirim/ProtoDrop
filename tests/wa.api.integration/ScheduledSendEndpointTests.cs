using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-002 US-022-01: POST /api/v1/transfers/{id}/send with scheduledSendAt (FR-022-3 gate, FR-022-4 store + emit now,
/// story AC "link is live now"). feature.scheduling is seeded off, so the gate-on class flips it BEFORE the first request
/// (the flags cache would otherwise hold the seeded value for its TTL); the gate-off class leaves the seed alone.
/// </summary>
public abstract class ScheduledSendEndpointTestsBase(bool gateOn) : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private BlobServiceClient _svc = null!;
    private WebApplicationFactory<Program> _factory = null!;
    protected WaDbContext Db = null!;
    protected HttpClient Client = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");
        // Same dummy broker as SendEndpointTests: the outbox row commits before the (unreachable) publish.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        Db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await Db.Database.MigrateAsync();

        if (gateOn)
        {
            var flag = await Db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Scheduling);
            flag.Value = "true";
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        _svc = new BlobServiceClient(accountUrl, new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await Db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Helpers ----

    protected async Task<Guid> SeedUserAsync(string planCode)
    {
        var planId = await Db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"sched-{Guid.NewGuid():N}@example.com",
            DisplayName = "Schedule Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Set<AppUser>().Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>TA-9.1 session cookie (same idiom as PlanUsageEndpointTests).</summary>
    private static string CookieFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        return "wa.session=" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }

    private HttpRequestMessage Req(HttpMethod method, string url, Guid? userId, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (userId is { } id) request.Headers.Add("Cookie", CookieFor(id));
        return request;
    }

    /// <summary>Draft → upload → finalize via the real endpoints. Returns the transfer id.</summary>
    protected async Task<Guid> SetupDraftTransferAsync(Guid? userId)
    {
        using var draftResponse = await Client.SendAsync(Req(HttpMethod.Post, "/api/v1/transfers/draft", userId,
            JsonContent.Create(new { files = new[] { new { name = "test.txt", sizeBytes = 512L, contentType = "text/plain" } } })));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        var bytes = new byte[512];
        RandomNumberGenerator.Fill(bytes);
        var cut = stagingPath.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(stagingPath[..cut]).GetBlobClient(stagingPath[(cut + 1)..]).UploadAsync(ms, true, default);

        var finalizePayload = JsonSerializer.Serialize(new { files = new[] { new { fileId, name = "test.txt", sizeBytes = 512L, contentType = "text/plain", retries = 0 } } });
        var finalizeRequest = Req(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize", userId,
            new StringContent(finalizePayload, Encoding.UTF8, "application/json"));
        finalizeRequest.Headers.Add("Idempotency-Key", $"key-finalize-{Guid.NewGuid():N}");
        using var finalizeResponse = await Client.SendAsync(finalizeRequest);
        Assert.Equal(HttpStatusCode.Created, finalizeResponse.StatusCode);

        using var finalizeDoc = JsonDocument.Parse(await finalizeResponse.Content.ReadAsByteArrayAsync());
        return new Guid(finalizeDoc.RootElement.GetProperty("id").GetString()!);
    }

    protected async Task<HttpResponseMessage> PostSendAsync(Guid transferId, string jsonBody, Guid? userId)
    {
        var request = Req(HttpMethod.Post, $"/api/v1/transfers/{transferId:N}/send", userId,
            new StringContent(jsonBody, Encoding.UTF8, "application/json"));
        request.Headers.Add("Idempotency-Key", $"key-send-{Guid.NewGuid():N}");
        return await Client.SendAsync(request);
    }

    protected static string ScheduleBody(DateTimeOffset due) =>
        JsonSerializer.Serialize(new { emails = new[] { "a@x.com" }, scheduledSendAt = due.ToString("o") });

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }

    protected async Task<Transfer> RowAsync(Guid transferId) =>
        await Db.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);

    protected async Task<DomainEvent?> CreatedEventAsync(string linkId) =>
        await Db.Set<DomainEvent>().AsNoTracking().FirstOrDefaultAsync(e => e.EventType == "transfer.created" && e.PartitionKey == linkId);

    protected async Task<HttpResponseMessage> GetAsync(string url, Guid? userId = null) =>
        await Client.SendAsync(Req(HttpMethod.Get, url, userId));
}

/// <summary>feature.scheduling flipped on before the first request.</summary>
public class ScheduledSendEndpointTests() : ScheduledSendEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task ProUser_Schedule_201_StatusScheduled_RowAndOutbox() // FR-022-4
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        var due = DateTimeOffset.UtcNow.AddHours(2);

        using var response = await PostSendAsync(transferId, ScheduleBody(due), userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        Assert.Equal(due.UtcDateTime, body.GetProperty("scheduledSendAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));

        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Scheduled, row.Status);
        Assert.NotNull(row.ScheduledSendAtUtc);
        Assert.Equal(due.UtcDateTime, row.ScheduledSendAtUtc!.Value, TimeSpan.FromSeconds(1));

        // transfer.created is emitted NOW, carrying scheduledSendAt (FR-022-4).
        var evt = await CreatedEventAsync(row.LinkId);
        Assert.NotNull(evt);
        using var envelope = JsonDocument.Parse(evt!.EnvelopeJson);
        var payload = envelope.RootElement.GetProperty("payload");
        Assert.Equal(due.UtcDateTime, payload.GetProperty("scheduledSendAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProUser_Scheduled_LinkIsLiveNow() // story AC: the link is live now
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task ProUser_Scheduled_PublicPage_CarriesUtcScheduledSendAt() // FR-022-6, EC-022-6 (UTC on the wire)
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        var due = DateTimeOffset.UtcNow.AddHours(2);
        using var send = await PostSendAsync(transferId, ScheduleBody(due), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("files").GetArrayLength());
        var wire = body.GetProperty("scheduledSendAt");
        Assert.Equal(JsonValueKind.String, wire.ValueKind);
        var parsed = wire.GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(due.UtcDateTime, parsed.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProUser_NoSchedule_PublicPage_ScheduledSendAtNull()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, "{\"emails\":[]}", userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("scheduledSendAt").ValueKind);
    }

    [Fact]
    public async Task ProUser_Scheduled_CountsTowardActiveTransfers() // D-5, D-6
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);

        using var response = await GetAsync("/api/v1/billing/plan", userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("activeTransfers").GetProperty("used").GetInt32());
        Assert.True(body.GetProperty("features").GetProperty("scheduling").GetBoolean());
    }

    [Fact]
    public async Task FreeUser_Schedule_403Forbidden_DraftUntouched() // FR-022-3
    {
        var userId = await SeedUserAsync("free");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task Guest_Schedule_403Forbidden() // FR-022-3: guests resolve to Free
    {
        var transferId = await SetupDraftTransferAsync(null);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task InvalidScheduledSendAt_400Validation()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, "{\"emails\":[],\"scheduledSendAt\":\"next tuesday\"}", userId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(TransferStatus.Draft, (await RowAsync(transferId)).Status);
    }

    [Fact]
    public async Task ProUser_NoSchedule_StaysActive() // regression: absent field = send now
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, "{\"emails\":[]}", userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("scheduledSendAt").ValueKind);
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Active, row.Status);
        Assert.Null(row.ScheduledSendAtUtc);
    }
}

/// <summary>Global feature.scheduling gate left at its seeded "false": even a Pro user is refused (FR-022-3).</summary>
public class ScheduledSendEndpointTests_GateOff() : ScheduledSendEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task GateOff_ProUser_Schedule_403Forbidden()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }
}
