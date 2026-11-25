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
