using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-044-02 (FR-044-7, AC-044-2, EC-044-6): GET /api/v1/features on the real host — anonymous, the six migrated gate rows read off,
/// a row set to "true" turns only that gate on, a deleted row degrades to off. Rows are changed before the first request (fresh host → cold FlagsCache).
/// </summary>
public class FeatureGatesEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;

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
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task Get_SeededDefaults_AllSixOff_Anonymous()
    {
        foreach (var key in FeatureGates.All)
            Assert.True(await _db.Set<FeatureFlag>().AsNoTracking().AnyAsync(f => f.Key == key), $"Gate '{key}' is not a migrated row.");

        var gates = await GetAsync();

        Assert.Equal(FeatureGates.All, gates.Keys.ToList());
        Assert.All(gates.Values, v => Assert.False(v));
    }

    [Fact]
    public async Task Get_DataExportTrue_OnlyThatGateOn() // AC-044-2 (read side)
    {
        var row = await _db.Set<FeatureFlag>().FirstAsync(f => f.Key == FeatureGates.DataExport);
        row.Value = "true";
        await _db.SaveChangesAsync();

        var gates = await GetAsync();

        Assert.True(gates[FeatureGates.DataExport]);
        Assert.Equal(1, gates.Values.Count(v => v));
    }

    [Fact]
    public async Task Get_RowDeleted_DegradesToOff() // EC-044-6
    {
        var row = await _db.Set<FeatureFlag>().FirstAsync(f => f.Key == FeatureGates.Search);
        _db.Remove(row);
        await _db.SaveChangesAsync();

        var gates = await GetAsync();

        Assert.False(gates[FeatureGates.Search]);
        Assert.Equal(6, gates.Count);
    }

    private async Task<Dictionary<string, bool>> GetAsync()
    {
        _factory = new TestFactory();
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/features"); // no cookie
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(await response.Content.ReadAsStringAsync())!;
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
