using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-001 US-021-01: org branding on GET /api/v1/public/transfers/{linkId}. AC-021-1 (page half), AC-021-3, EC-021-1,
/// plus the AddAppUserOrgBranding schema. feature.branding is seeded off, so each test flips it BEFORE the first request.
/// </summary>
public class PublicTransferBrandingTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private string _connString = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", _connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        await _db.Database.MigrateAsync();

        // Gate on before the first request: the flags cache would otherwise hold the seeded "false" for its TTL.
        var flag = await _db.Set<FeatureFlag>().SingleAsync(f => f.Key == "feature.branding");
        flag.Value = "true";
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task ProOwnerWithOrg_GateOn_ReturnsOrgName() // AC-021-1 page half
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        await SeedActiveTransferAsync(ownerId, "b1b2c3d4");

        var root = await GetAsync("b1b2c3d4");

        Assert.Equal("Alice", root.GetProperty("from").GetString());
        Assert.Equal("Studio Nova", root.GetProperty("orgName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("orgLogoUrl").ValueKind);
    }

    [Fact]
    public async Task FreeOwnerWithOrg_ReturnsOrgNameNull() // AC-021-3
    {
        var ownerId = await SeedOwnerAsync("free", "Studio Nova");
        await SeedActiveTransferAsync(ownerId, "c1b2c3d4");

        var root = await GetAsync("c1b2c3d4");

        Assert.Equal("Alice", root.GetProperty("from").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("orgName").ValueKind);
    }

    [Fact]
    public async Task OrgNameChangedAfterSend_NextViewShowsCurrent() // EC-021-1
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        await SeedActiveTransferAsync(ownerId, "d1b2c3d4");

        var first = await GetAsync("d1b2c3d4");
        Assert.Equal("Studio Nova", first.GetProperty("orgName").GetString());

        await _db.Set<AppUser>().Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgName, "Nova Labs"));

        var second = await GetAsync("d1b2c3d4");
        Assert.Equal("Nova Labs", second.GetProperty("orgName").GetString());
    }

    [Fact]
    public async Task Migration_AddsNullableOrgColumns()
    {
        await using var conn = new SqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS " +
                          "WHERE TABLE_NAME = 'AppUser' AND COLUMN_NAME IN ('OrgName', 'OrgLogoBlobPath')";
        var cols = new Dictionary<string, (string Type, int Length, string Nullable)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                cols[reader.GetString(0)] = (reader.GetString(1), reader.GetInt32(2), reader.GetString(3));
        }

        Assert.Equal(("nvarchar", 60, "YES"), cols["OrgName"]);
        Assert.Equal(("varchar", 400, "YES"), cols["OrgLogoBlobPath"]);
    }

    private async Task<JsonElement> GetAsync(string linkId)
    {
        using var response = await _client.GetAsync($"/api/v1/public/transfers/{linkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }

    private async Task<Guid> SeedOwnerAsync(string planCode, string? orgName)
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"branding-{Guid.NewGuid():N}@example.com",
            DisplayName = "Alice",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow,
            OrgName = orgName
        };
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedActiveTransferAsync(Guid ownerId, string linkId)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = linkId,
            Status = 1,
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
