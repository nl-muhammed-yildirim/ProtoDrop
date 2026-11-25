using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-044-01 (FR-044-5/6, AC-044-1, AC-044-3, EC-044-1, EC-044-5): PUT/GET /api/v1/admin/flags — typed validation,
/// FLAG_UNKNOWN before FLAG_INVALID, old value stays live on rejection, audit {old,new} on success, and a limit edit
/// reaching ILimitsProvider within the real 30 s TTL without a restart.
/// </summary>
public class AdminFlagsEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string AdminEmail = "flags-admin@x.com";
    private const string FreeMaxTransferSizeKey = "limits.free.maxTransferSize";
    private const string SeedValue = "5368709120";   // migration seed for limits.free.maxTransferSize
    private const string NewValue = "10737418240";   // 10 GB — the edit written by the tests

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _adminUserId;

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
        // Role derives per request from Wa:AdminEmails (T-011-01 gate) — must be set before the host builds.
        Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail);
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        var planId = await _db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();
        _adminUserId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _adminUserId,
            Email = AdminEmail.ToLowerInvariant(),
            DisplayName = "Flags Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _factory = new TestFactory();
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
    public async Task Put_ValidLimit_Returns200NewValue_AuditRowHasOldAndNew_GetShowsOverridden()
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/v1/admin/flags/" + FreeMaxTransferSizeKey, NewValue);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(FreeMaxTransferSizeKey, doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(NewValue, doc.RootElement.GetProperty("value").GetString());
        }

        Assert.Equal(NewValue, await StoredValueAsync(FreeMaxTransferSizeKey));

        var audit = await _db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == "flag.set").ToListAsync();
        var row = Assert.Single(audit);
        Assert.Equal(FreeMaxTransferSizeKey, row.EntityId);
        Assert.Equal(AdminEmail.ToLowerInvariant(), row.ActorEmail?.ToLowerInvariant());
        using (var details = JsonDocument.Parse(row.DetailsJson!))
        {
            Assert.Equal(SeedValue, details.RootElement.GetProperty("oldValue").GetString());
            Assert.Equal(NewValue, details.RootElement.GetProperty("newValue").GetString());
        }

        using var get = await SendAsync(HttpMethod.Get, "/api/v1/admin/flags");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var list = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var flag = list.RootElement.EnumerateArray().First(e => e.GetProperty("key").GetString() == FreeMaxTransferSizeKey);
        Assert.Equal(NewValue, flag.GetProperty("value").GetString());
        Assert.Equal("int", flag.GetProperty("type").GetString());
        Assert.Equal(SeedValue, flag.GetProperty("planDefault").GetString());
        Assert.True(flag.GetProperty("overridden").GetBoolean());
    }

    [Fact]
    public async Task Put_UnknownKey_Returns404FlagUnknown()
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/v1/admin/flags/limits.free.nonexistent", NewValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FLAG_UNKNOWN", doc.RootElement.GetProperty("code").GetString());
        Assert.Empty(await _db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == "flag.set").ToListAsync());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("\"abc\"")]
    public async Task Put_StringForIntegerLimit_Returns400FlagInvalid_PreviousValueStillEffective(string body)
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/v1/admin/flags/" + FreeMaxTransferSizeKey, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FLAG_INVALID", doc.RootElement.GetProperty("code").GetString());

        Assert.Equal(SeedValue, await StoredValueAsync(FreeMaxTransferSizeKey));
        Assert.Empty(await _db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == "flag.set").ToListAsync());

        var limits = await _factory.Services.GetRequiredService<ILimitsProvider>().Resolve("free");
        Assert.Equal(long.Parse(SeedValue), limits.MaxTransferSize);
    }

    [Fact]
    public async Task Put_NonPositiveLimit_Returns400FlagInvalid()
    {
        using var response = await SendAsync(HttpMethod.Put, "/api/v1/admin/flags/limits.free.maxDownloads", "0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FLAG_INVALID", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_Limit_VisibleToNewResolution_WithinTtl()
    {
        var provider = _factory.Services.GetRequiredService<ILimitsProvider>();
        Assert.Equal(long.Parse(SeedValue), (await provider.Resolve("free")).MaxTransferSize); // warms FlagsCache

        using var response = await SendAsync(HttpMethod.Put, "/api/v1/admin/flags/" + FreeMaxTransferSizeKey, NewValue);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Real TTL (TA-3.4 / AC-044-1) — same approach as FlagOverrideIntegrationTests; do not shorten.
        await Task.Delay(31_000);

        Assert.Equal(long.Parse(NewValue), (await provider.Resolve("free")).MaxTransferSize);
    }

    [Fact]
    public async Task AllSeededRows_PassFlagValueRules()
    {
        var rows = await _db.Set<FeatureFlag>().AsNoTracking().ToListAsync();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
            Assert.True(FlagValueRules.IsValid(row.Key, row.Value), $"Seeded flag '{row.Key}' = '{row.Value}' fails FlagValueRules.");
    }

    private async Task<string> StoredValueAsync(string key) =>
        (await _db.Set<FeatureFlag>().AsNoTracking().FirstAsync(f => f.Key == key)).Value;

    // TA-9.1 session cookie forged with the test secret (same idiom as AdminOverviewEmailHealthTests).
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body = null)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_adminUserId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
