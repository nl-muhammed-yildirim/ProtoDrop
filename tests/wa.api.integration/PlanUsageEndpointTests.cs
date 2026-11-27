using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-020-03 (AC-020-3): GET /api/v1/billing/plan — plan code + live usage vs. PlanContext limits.
/// Fresh Azurite + SQL 2022 per instance — same pattern as PortalEndpointTests.
/// </summary>
public class PlanUsageEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string Path = "/api/v1/billing/plan";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

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

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task NoCookie_401Unauthenticated()
    {
        using var response = await _client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHENTICATED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Free_LiveUsage_CountsActiveAndLimitReached_ExcludesDraftAndExpired()
    {
        var userId = await SeedUserAsync("free");
        await SeedTransferAsync(userId, 1, 1000);
        await SeedTransferAsync(userId, 1, 2000);
        await SeedTransferAsync(userId, 3, 4000);   // DownloadLimit still holds storage and counts (D-3)
        await SeedTransferAsync(userId, 0, 8000);   // Draft: excluded
        await SeedTransferAsync(userId, 2, 16000);  // Expired: excluded

        var limits = await LimitsOfAsync("free");

        using var response = await GetAsync(userId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("free", root.GetProperty("planCode").GetString());
        Assert.Equal(7000, root.GetProperty("storage").GetProperty("usedBytes").GetInt64());
        Assert.Equal(limits.StorageQuota, root.GetProperty("storage").GetProperty("allowedBytes").GetInt64());
        Assert.Equal(3, root.GetProperty("activeTransfers").GetProperty("used").GetInt32());
        Assert.Equal(limits.ActiveTransfersMax, root.GetProperty("activeTransfers").GetProperty("allowed").GetInt32());
        Assert.Equal(5368709120L, root.GetProperty("limits").GetProperty("maxTransferSize").GetInt64());
        Assert.Equal(5368709120L, root.GetProperty("limits").GetProperty("maxSingleFile").GetInt64());
    }

    [Fact]
    public async Task Business_ActiveTransfersAllowedIsNull()
    {
        var userId = await SeedUserAsync("business");
        var limits = await LimitsOfAsync("business");

        using var response = await GetAsync(userId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("business", root.GetProperty("planCode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("activeTransfers").GetProperty("allowed").ValueKind);
        Assert.Equal(limits.StorageQuota, root.GetProperty("storage").GetProperty("allowedBytes").GetInt64());
    }

    [Fact]
    public async Task PlanFlip_NextRequestReflectsNewLimits()
    {
        var userId = await SeedUserAsync("free");
        var freeLimits = await LimitsOfAsync("free");
        var proLimits = await LimitsOfAsync("pro");

        using (var first = await GetAsync(userId))
        {
            using var doc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            Assert.Equal(freeLimits.StorageQuota, doc.RootElement.GetProperty("storage").GetProperty("allowedBytes").GetInt64());
        }

        var proId = await _db.Set<Plan>().Where(p => p.Code == "pro").Select(p => p.Id).FirstAsync();
        await _db.Set<AppUser>().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PlanId, proId));

        using var second = await GetAsync(userId);
        using var doc2 = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("pro", doc2.RootElement.GetProperty("planCode").GetString());
        Assert.Equal(proLimits.StorageQuota, doc2.RootElement.GetProperty("storage").GetProperty("allowedBytes").GetInt64());
        Assert.Equal(proLimits.ActiveTransfersMax, doc2.RootElement.GetProperty("activeTransfers").GetProperty("allowed").GetInt32());
        Assert.Equal(21474836480L, doc2.RootElement.GetProperty("limits").GetProperty("maxTransferSize").GetInt64());
        Assert.Equal(21474836480L, doc2.RootElement.GetProperty("limits").GetProperty("maxSingleFile").GetInt64());
    }

    // ---- Helpers ----

    private async Task<LimitsRecord> LimitsOfAsync(string planCode)
    {
        var json = await _db.Set<Plan>().AsNoTracking().Where(p => p.Code == planCode).Select(p => p.LimitsJson).FirstAsync();
        return JsonSerializer.Deserialize<LimitsRecord>(json)!;
    }

    private async Task<Guid> SeedUserAsync(string planCode)
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"plan-usage-{Guid.NewGuid():N}@example.com",
            DisplayName = "Plan Usage Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Transfer + BlobRef + FileItem with an explicit Status (FinalizeAccountLimitsTests seed idiom).</summary>
    private async Task SeedTransferAsync(Guid ownerId, byte status, long bytes)
    {
        var now = DateTime.UtcNow;
        var transferId = Guid.NewGuid();
        var blobRef = new BlobRef
        {
            Id = Guid.NewGuid(),
            BlobPath = $"transfers/{transferId:N}/{Guid.NewGuid():N}",
            SizeBytes = bytes,
            RefCount = 1,
            CreatedAtUtc = now
        };
        _db.Set<BlobRef>().Add(blobRef);
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = wa.domain.ValueObjects.LinkId.Mint().Value,
            Status = status,
            OwnerAppUserId = ownerId,
            TotalBytes = bytes,
            FileCount = 1,
            SenderName = "Guest",
            MaxDownloads = 1,
            ExpiresAtUtc = now.AddDays(30),
            CreatedAtUtc = now
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobRef.Id,
            OriginalName = "seeded.txt",
            SizeBytes = bytes,
            SortOrder = 0
        });
        await _db.SaveChangesAsync();
    }

    // TA-9.1 cookie idiom (same as PortalEndpointTests)
    private async Task<HttpResponseMessage> GetAsync(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        return await _client.SendAsync(request);
    }
}
