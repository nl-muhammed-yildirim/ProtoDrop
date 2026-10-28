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
using wa.domain.Errors;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-TRF-007 review fix — e2e coverage for AC-007-2 (storage quota) and AC-007-3 (active-transfers cap)
/// via the TA-9.1 provisional session cookie: the finalize endpoint must run account-level checks when a
/// valid wa.session cookie is present, and skip them for guests (no cookie) — EC-007-3.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as FinalizeEndpointTests.
/// </summary>
public class FinalizeAccountLimitsTests : IAsyncLifetime
{
    // Class-level constants — values per seeded flag row, not plan literals (TA-3.4).
    private const long FreeStorageQuota = 5_368_709_120L;        // 5 GB — migration seed for limits.free.storageQuota / Plan free LimitsJson
    private const int FreeActiveTransfersMax = 20;               // 20 — migration seed for limits.free.activeTransfersMax
    private const string CookieName = "wa.session";
    private const string JwtSecret = "test-jwt-secret";          // pinned in InitializeAsync below — same value the app reads from Wa:Jwt:Secret

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;
    private Uri _accountUrl = null!;
    private BlobServiceClient _svc = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", _accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", JwtSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // TA-9.1 test: Cookie headers are set explicitly per request — the default cookie container would overwrite them
        });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- AC-007-2: storage-quota check runs on the finalize endpoint when a session cookie is present ----

    [Fact]
    public async Task Finalize_WithSessionCookie_OverStorageQuota_Returns507AndNoTransferRow()
    {
        var ownerId = await SeedOwnerAsync();

        // TA-3.4 seeded rows: one active transfer (Status=1) at FreeStorageQuota - 500 bytes → adding any new transfer (even 1024B) exceeds quota.
        long bytesA = FreeStorageQuota - 500L;
        await SeedActiveTransferRowsAsync(ownerId, [bytesA]);

        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("quota.txt");
        using var response = await PostFinalizeAsync(draftId, fileId, "ac72-507", CookieValueFor(ownerId));

        // ProblemCodes pins STORAGE_QUOTA_EXCEEDED at 507 (no HttpStatusCode enum value — compare the int).
        Assert.Equal(507, (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.StorageQuotaExceeded, doc.RootElement.GetProperty("code").GetString());

        // Quota check throws before any DB writes — only the 1 seeded row exists.
        Assert.Equal(1, await _db.Set<Transfer>().CountAsync());
    }

    [Fact]
    public async Task Finalize_WithoutCookie_UnderSameSeededLoad_Succeeds_GuestPathSkipsQuota()
    {
        var ownerId = await SeedOwnerAsync();
        long bytesA = FreeStorageQuota - 200_000_000L;
        long bytesB = 150_000_000L;
        await SeedActiveTransferRowsAsync(ownerId, [bytesA, bytesB]);

        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("guest-quota.txt");
        using var response = await PostFinalizeAsync(draftId, fileId, "ac72-guest-ok", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Guest row landed despite seeded load exceeding the owner's free quota (EC-007-3).
        Assert.Equal(3, await _db.Set<Transfer>().CountAsync());
    }

    // ---- AC-007-3: active-transfers cap runs on the finalize endpoint when a session cookie is present ----

    [Fact]
    public async Task Finalize_WithSessionCookie_AtActiveTransfersCap_ReturnsRejectionAndNoTransferRow()
    {
        var ownerId = await SeedOwnerAsync();

        // Exactly FreeActiveTransfersMax (20) active rows → the 21st finalize must be rejected.
        for (var i = 0; i < FreeActiveTransfersMax; i++)
            await SeedActiveTransferRowsAsync(ownerId, [1024L]);

        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("cap.txt");
        using var response = await PostFinalizeAsync(draftId, fileId, "ac73-reject", CookieValueFor(ownerId));

        // Program.cs maps ActiveTransfersExceededException → ProblemCodes.Validation (400 VALIDATION).
        // Smell noted for FR-056: no dedicated closed code exists — pinning the production mapping as-is.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.Validation, doc.RootElement.GetProperty("code").GetString());

        // Count check precedes mutation — only the 20 seeded rows exist.
        Assert.Equal(FreeActiveTransfersMax, await _db.Set<Transfer>().CountAsync());
    }

    [Fact]
    public async Task Finalize_WithoutCookie_UnderSameSeededLoad_Succeeds_GuestPathSkipsCap()
    {
        var ownerId = await SeedOwnerAsync();
        for (var i = 0; i < FreeActiveTransfersMax; i++)
            await SeedActiveTransferRowsAsync(ownerId, [1024L]);

        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("guest-cap.txt");
        using var response = await PostFinalizeAsync(draftId, fileId, "ac73-guest-ok", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // Guest row landed despite cap being hit for that owner (EC-007-3).
        Assert.Equal(FreeActiveTransfersMax + 1, await _db.Set<Transfer>().CountAsync());
    }

    // ---- Helpers ----

    /// <summary>Seed an AppUser on the free plan — required because Transfer.OwnerAppUserId has FK_Transfer_Owner.</summary>
    private async Task<Guid> SeedOwnerAsync()
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == ILimitsProvider.GuestPlanCode)
            .Select(p => p.Id).FirstAsync();
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"ac007-{Guid.NewGuid():N}@example.com",
            DisplayName = "AC007 Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(owner);
        await _db.SaveChangesAsync();
        return owner.Id;
    }

    /// <summary>Seed Transfer rows directly in the DB (TA-3.4 seeded rows) — Status=1 active, no blobs needed:
    /// both checks read only OwnerAppUserId/Status/TotalBytes and run before any blob copy.</summary>
    private async Task SeedActiveTransferRowsAsync(Guid ownerId, long[] totalBytes)
    {
        var transfers = totalBytes.Select(bytes => new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = wa.domain.ValueObjects.LinkId.Mint().Value,
            Status = 1, // Active — inside GetActiveStorageBytesAsync/GetActiveTransferCountAsync WHERE (Status IN (1,3))
            OwnerAppUserId = ownerId,
            TotalBytes = bytes,
            FileCount = 1,
            SenderName = "Guest",
            MaxDownloads = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();
        _db.Set<Transfer>().AddRange(transfers);
        await _db.SaveChangesAsync();
    }

    /// <summary>TA-9.1 provisional scheme: Base64(userId|exp|sig), sig = HMAC-SHA256(userId|exp, Wa:Jwt:Secret).</summary>
    private static string CookieValueFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(JwtSecret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes($"{userId:N}|{exp}")));
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{userId:N}|{exp}|{sig}"));
    }

    private async Task<(Guid DraftId, Guid FileId)> SetupDraftAndStagedBlobAsync(string name)
    {
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = new[] { new { name, sizeBytes = 1024L, contentType = "text/plain" } } });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        var bytes = new byte[1024];
        RandomNumberGenerator.Fill(bytes);
        var path = BlobPaths.StagingFile(draftId, fileId);
        var cut = path.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(path[..cut]).GetBlobClient(path[(cut + 1)..])
                .UploadAsync(ms, true, default);

        return (draftId, fileId);
    }

    private async Task<HttpResponseMessage> PostFinalizeAsync(Guid draftId, Guid fileId, string idempotencyKey, string? sessionCookie)
    {
        var payload = JsonSerializer.Serialize(new { files = new[] { new { fileId, name = "x.txt", sizeBytes = 1024L, contentType = "text/plain", retries = 0 } } });
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (sessionCookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={sessionCookie}");
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
