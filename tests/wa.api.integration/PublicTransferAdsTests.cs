using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-004 US-024-02: adsEnabled on GET /api/v1/public/transfers/{linkId}. AC-024-2 (Pro/Business never get an ad).
/// feature.ads is seeded off, so it is flipped BEFORE the first request (30 s flags cache). The Free plan row
/// carries ads:true from the AddFreeAdsEnabled seed migration.
/// </summary>
public class PublicTransferAdsTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;
    private const string TestSecret = "test-jwt-secret";

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

        // Gate on before the first request: the flags/plan caches would otherwise hold the seeded values for their TTL.
        var flag = await _db.Set<FeatureFlag>().SingleAsync(f => f.Key == "feature.ads");
        flag.Value = "true";
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task FreeOwner_FlagOn_AdsEnabledTrue()
    {
        var ownerId = await SeedOwnerAsync(Plan.DefaultCode);
        await SeedTransferAsync(ownerId, "a1b2c3d4");

        var root = await GetAsync("a1b2c3d4");

        Assert.True(root.GetProperty("adsEnabled").GetBoolean());
    }

    [Fact]
    public async Task GuestTransfer_FlagOn_AdsEnabledTrue()
    {
        await SeedTransferAsync(null, "b1b2c3d4");

        var root = await GetAsync("b1b2c3d4");

        Assert.True(root.GetProperty("adsEnabled").GetBoolean());
    }

    [Fact]
    public async Task ProOwner_FlagOn_AdsEnabledFalse() // AC-024-2
    {
        var ownerId = await SeedOwnerAsync("pro");
        await SeedTransferAsync(ownerId, "c1b2c3d4");

        var root = await GetAsync("c1b2c3d4");

        Assert.False(root.GetProperty("adsEnabled").GetBoolean());
    }

    [Fact]
    public async Task BusinessOwner_FlagOn_AdsEnabledFalse() // AC-024-2
    {
        var ownerId = await SeedOwnerAsync("business");
        await SeedTransferAsync(ownerId, "d1b2c3d4");

        var root = await GetAsync("d1b2c3d4");

        Assert.False(root.GetProperty("adsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Expired_NoAdsEnabledProperty()
    {
        await SeedTransferAsync(null, "e1b2c3d4", status: 2);

        using var response = await _client.GetAsync("/api/v1/public/transfers/e1b2c3d4");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.False(doc.RootElement.TryGetProperty("adsEnabled", out _));
    }

    private async Task<JsonElement> GetAsync(string linkId)
    {
        using var response = await _client.GetAsync($"/api/v1/public/transfers/{linkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }

    private async Task<Guid> SeedOwnerAsync(string planCode)
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"ads-{Guid.NewGuid():N}@example.com",
            DisplayName = "Alice",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedTransferAsync(Guid? ownerId, string linkId, byte status = 1)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = linkId,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 5,
            DownloadsCount = 0,
            SenderName = "Alice",
            OwnerAppUserId = ownerId,
            TotalBytes = 0,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
