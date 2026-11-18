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
/// US-047-03 (FR-047-6): GET /api/v1/admin/erasures — wire shape, SuperAdmin (DPO) sees the full email,
/// Operator sees the masked form, 401 without a session, 400 on a bad cursor / limit.
/// </summary>
public class AdminErasuresEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string SuperEmail = "erasure-super@x.com";
    private const string OperatorEmail = "erasure-operator@x.com";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _superId;
    private Guid _operatorId;

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
        // Program.cs reads both lists at host build — set before the first CreateClient.
        Environment.SetEnvironmentVariable("Wa__AdminEmails", SuperEmail);
        Environment.SetEnvironmentVariable("Wa__OperatorEmails", OperatorEmail);
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=***");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        var options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options;
        _db = new WaDbContext(options);
        await _db.Database.MigrateAsync();

        var planId = await _db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();
        _superId = await AddUserAsync(SuperEmail, planId);
        _operatorId = await AddUserAsync(OperatorEmail, planId);
        var dana = await AddUserAsync("dana@x.com", planId);
        await using (var ctx = new WaDbContext(options))
        {
            Assert.True(await new AppUserStore(ctx).SoftDeleteWithReHomeAsync(dana, DateTime.UtcNow, CancellationToken.None));
        }

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

    private async Task<Guid> AddUserAsync(string email, Guid planId)
    {
        var id = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser { Id = id, Email = email, DisplayName = "U", PlanId = planId, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task SuperAdmin_SeesFullEmail_AndWireShape()
    {
        var res = await GetAsync("/api/v1/admin/erasures", _superId);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("nextCursor", out _));
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        foreach (var key in new[] { "userId", "email", "deletedAtUtc", "reHomedTransferCount", "sweepStatus", "sweptAtUtc" })
            Assert.True(item.TryGetProperty(key, out _), key);
        Assert.Equal("dana@x.com", item.GetProperty("email").GetString());
        Assert.Equal("pending", item.GetProperty("sweepStatus").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("sweptAtUtc").ValueKind);
        Assert.EndsWith("Z", item.GetProperty("deletedAtUtc").GetString()); // review m2: UTC kind so browsers do not parse as local time
    }

    [Fact]
    public async Task Operator_SeesMaskedEmail()
    {
        var res = await GetAsync("/api/v1/admin/erasures", _operatorId);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("d****@x.com", item.GetProperty("email").GetString());
    }

    [Fact]
    public async Task NoSession_401_Unauthenticated()
    {
        var res = await GetAsync("/api/v1/admin/erasures", null);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task MalformedCursor_400_Validation()
    {
        var res = await GetAsync("/api/v1/admin/erasures?cursor=not-base64", _superId);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("VALIDATION", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LimitOverPageSize_400_Validation()
    {
        var res = await GetAsync("/api/v1/admin/erasures?limit=26", _superId);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("VALIDATION", await res.Content.ReadAsStringAsync());
    }

    // TA-9.1 cookie idiom (same as AdminOverviewEmailHealthTests).
    private async Task<HttpResponseMessage> GetAsync(string url, Guid? userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (userId is { } id)
        {
            var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
            var unsigned = $"{id:N}|{exp}";
            var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
            var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
            request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        }
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
