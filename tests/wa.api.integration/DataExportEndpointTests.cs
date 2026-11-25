using System.Net;
using System.Security.Cryptography;
using System.Text;
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
/// US-047-01 (AC-047-1, FR-047-4, EC-047-2, EC-047-6): POST/GET /api/v1/auth/me/export over a real host.
/// Fresh Azurite + SQL 2022 per fact (cold FlagsCache); each fact stays within the 5/min auth rate rule (≤ 4 requests).
/// </summary>
public class DataExportEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}").ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        var planId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = "export-user@x.com",
            DisplayName = "Export User",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    /// <summary>Starts the host AFTER the flag row is set (the FlagsCache is cold per fact).</summary>
    private async Task StartAsync(bool flagOn)
    {
        var flag = await _db.Set<FeatureFlag>().FirstAsync(f => f.Key == FeatureGates.DataExport);
        flag.Value = flagOn ? "true" : "false";
        await _db.SaveChangesAsync();
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private HttpRequestMessage Req(HttpMethod method, bool signedIn = true)
    {
        var request = new HttpRequestMessage(method, "/api/v1/auth/me/export");
        if (signedIn)
        {
            var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
            var unsigned = $"{_userId:N}|{exp}";
            var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
            request.Headers.Add("Cookie", $"wa.session={Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"))}");
        }
        return request;
    }

    private async Task<DataExport> SeedAsync(byte state)
    {
        var row = new DataExport
        {
            Id = Guid.NewGuid(),
            AppUserId = _userId,
            State = state,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            BlobPath = state == DataExportState.Ready ? $"exports/{_userId:N}/x.zip" : null,
            ReadyAtUtc = state == DataExportState.Ready ? DateTime.UtcNow.AddMinutes(-5) : null,
        };
        _db.Set<DataExport>().Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Post_FlagOff_Returns404FeatureDisabled()
    {
        await StartAsync(flagOn: false);
        using var response = await _client!.SendAsync(Req(HttpMethod.Post));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("FEATURE_DISABLED", (await JsonOf(response)).GetProperty("code").GetString());
        Assert.False(await _db.Set<DataExport>().AnyAsync());
    }

    [Fact]
    public async Task Post_Guest_FlagOn_Returns401()
    {
        await StartAsync(flagOn: true);
        using var response = await _client!.SendAsync(Req(HttpMethod.Post, signedIn: false));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Twice_Returns202ThenExportInFlight409_OneRow() // EC-047-6
    {
        await StartAsync(flagOn: true);
        using var first = await _client!.SendAsync(Req(HttpMethod.Post));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal("processing", (await JsonOf(first)).GetProperty("state").GetString());

        using var second = await _client.SendAsync(Req(HttpMethod.Post));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("EXPORT_IN_FLIGHT", (await JsonOf(second)).GetProperty("code").GetString());

        var rows = await _db.Set<DataExport>().AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(DataExportState.Processing, row.State);
    }

    [Fact]
    public async Task Get_NoExport_Returns204()
    {
        await StartAsync(flagOn: true);
        using var response = await _client!.SendAsync(Req(HttpMethod.Get));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Get_Ready_ReturnsDownloadUrl()
    {
        await StartAsync(flagOn: true);
        var row = await SeedAsync(DataExportState.Ready);
        using var response = await _client!.SendAsync(Req(HttpMethod.Get));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("ready", body.GetProperty("state").GetString());
        var url = body.GetProperty("downloadUrl").GetString()!;
        Assert.Contains($"/exports/{_userId:N}/{row.Id:N}.zip", url);
        Assert.Contains("sig=", url);
        var se = System.Net.WebUtility.UrlDecode(url.Split("se=")[1].Split('&')[0]);
        var expiry = DateTime.Parse(se, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.InRange((expiry - DateTime.UtcNow).TotalHours, 23.9, 24.1);
    }

    [Fact]
    public async Task Get_Failed_ThenPost_CreatesNewJob() // EC-047-2
    {
        await StartAsync(flagOn: true);
        await SeedAsync(DataExportState.Failed);
        using var get = await _client!.SendAsync(Req(HttpMethod.Get));
        Assert.Equal("failed", (await JsonOf(get)).GetProperty("state").GetString());

        using var post = await _client.SendAsync(Req(HttpMethod.Post));
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        Assert.Equal(2, await _db.Set<DataExport>().CountAsync());
    }

    [Fact]
    public async Task Get_FlagOff_StillReturnsState() // FR-047-4: the gate stops creation, not reading
    {
        await StartAsync(flagOn: false);
        await SeedAsync(DataExportState.Ready);
        using var response = await _client!.SendAsync(Req(HttpMethod.Get));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ready", (await JsonOf(response)).GetProperty("state").GetString());
    }
}
