using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-003 US-023-01: GET /api/v1/transfers/{id}/analytics (FR-023-1 aggregates, FR-023-5 gate, AC-023-1/2/3, EC-023-1..6).
/// feature.analytics is seeded off, so the gate-on class flips it BEFORE the first request (flags cache TTL); the gate-off class
/// leaves the seed alone. Same container recipe as ScheduledSendEndpointTests.
/// </summary>
public abstract class TransferAnalyticsEndpointTestsBase(bool gateOn) : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    protected MsSqlContainer Sql = null!;
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
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        Sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await Sql.StartAsync();
        var connString = Sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        Db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await Db.Database.MigrateAsync();

        if (gateOn)
        {
            var flag = await Db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Analytics);
            flag.Value = "true";
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        var svc = new BlobServiceClient(accountUrl, new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await Db.DisposeAsync();
        await Sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Helpers ----

    protected async Task<Guid> SeedUserAsync(string planCode)
    {
        var planId = await Db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"analytics-{Guid.NewGuid():N}@example.com",
            DisplayName = "Analytics Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Set<AppUser>().Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    protected async Task<Guid> SeedTransferAsync(Guid? ownerId, byte status = TransferStatus.Active, DateTime? createdUtc = null, bool deleted = false)
    {
        var id = Guid.NewGuid();
        var created = createdUtc ?? DateTime.UtcNow.AddDays(-10);
        Db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = Guid.NewGuid().ToString("N")[..8],
            OwnerAppUserId = ownerId,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 1000,
            SenderName = "Anna K.",
            TotalBytes = 1,
            FileCount = 1,
            CreatedAtUtc = created,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        });
        await Db.SaveChangesAsync();
        return id;
    }

    protected static DownloadEvent Event(Guid transferId, DateTime atUtc, string? country, string? family) => new()
    {
        TransferId = transferId,
        IpHash = "IPHASH-SENTINEL",
        UaHash = "UAHASH-SENTINEL",
        Country = country,
        UserAgentFamily = family,
        CreatedAtUtc = atUtc
    };

    protected async Task AddEventsAsync(params DownloadEvent[] events)
    {
        Db.Set<DownloadEvent>().AddRange(events);
        await Db.SaveChangesAsync();
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

    protected async Task<HttpResponseMessage> GetAsync(string url, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (userId is { } id) request.Headers.Add("Cookie", CookieFor(id));
        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> AnalyticsAsync(Guid transferId, Guid? userId) =>
        GetAsync($"/api/v1/transfers/{transferId}/analytics", userId);

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }
}

/// <summary>feature.analytics flipped on before the first request.</summary>
public class TransferAnalyticsEndpointTests() : TransferAnalyticsEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task ProUser_WithDownloads_200_DailyTopCountryTopBrowserTotal() // AC-023-1 / story AC 1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var today = DateTime.UtcNow.Date.AddHours(1);
        await AddEventsAsync(
            Event(id, today, "NL", "Chrome"), Event(id, today.AddDays(-1), "NL", "Chrome"), Event(id, today.AddDays(-2), "NL", "Firefox"),
            Event(id, today, "DE", "Chrome"),
            Event(id, today, null, null), Event(id, today.AddDays(-1), null, null));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(6, body.GetProperty("total").GetInt32());
        var countries = body.GetProperty("countries").EnumerateArray().Select(c => (c.GetProperty("code").GetString(), c.GetProperty("count").GetInt32())).ToList();
        Assert.Equal([("NL", 3), ("??", 2), ("DE", 1)], countries);
        Assert.Equal("Chrome", body.GetProperty("browsers")[0].GetProperty("family").GetString());
        var daily = body.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), daily[^1].GetProperty("date").GetString());
        Assert.Equal(3, daily[^1].GetProperty("count").GetInt32());
        Assert.Equal(daily.Sum(d => d.GetProperty("count").GetInt32()), body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ProUser_Body_HasNoRawIpUaOrHashes() // AC-023-2
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), "NL", "Chrome"));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENTINEL", raw);
        Assert.DoesNotContain("Mozilla", raw);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(["total", "daily", "countries", "browsers"], doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task ProUser_ZeroDownloads_200_EmptyState() // EC-023-1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.All(body.GetProperty("daily").EnumerateArray(), d => Assert.Equal(0, d.GetProperty("count").GetInt32()));
        Assert.Equal(0, body.GetProperty("countries").GetArrayLength());
        Assert.Equal(0, body.GetProperty("browsers").GetArrayLength());
    }

    [Fact]
    public async Task ProUser_NullCountry_Unknown_NullFamily_Other() // EC-023-2/3
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), null, null));

        var body = await ReadJsonAsync(await AnalyticsAsync(id, userId));

        Assert.Equal("??", body.GetProperty("countries")[0].GetProperty("code").GetString());
        Assert.Equal("Other", body.GetProperty("browsers")[0].GetProperty("family").GetString());
    }

    [Theory]
    [InlineData(TransferStatus.Expired)]
    [InlineData(TransferStatus.DownloadLimit)]
    public async Task ProUser_ExpiredTransfer_200(byte status) // EC-023-5
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId, status);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), "NL", "Chrome"));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ProUser_EventOlderThanWindow_Excluded()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow.AddDays(-1500));
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddDays(-1000), "NL", "Chrome"), Event(id, DateTime.UtcNow.AddHours(-1), "DE", "Chrome"));

        var body = await ReadJsonAsync(await AnalyticsAsync(id, userId));

        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal("DE", body.GetProperty("countries")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task FreeUser_403Forbidden() // AC-023-3 server half / EC-023-6
    {
        var userId = await SeedUserAsync("free");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Guest_401()
    {
        var id = await SeedTransferAsync(null);

        using var response = await AnalyticsAsync(id, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOwner_404NotFound()
    {
        var owner = await SeedUserAsync("pro");
        var other = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(owner);

        using var response = await AnalyticsAsync(id, other);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeletedTransfer_404NotFound()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId, deleted: true);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProUser_BillingPlan_FeaturesAnalyticsTrue()
    {
        var userId = await SeedUserAsync("pro");

        var body = await ReadJsonAsync(await GetAsync("/api/v1/billing/plan", userId));

        Assert.True(body.GetProperty("features").GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public async Task FreeUser_BillingPlan_FeaturesAnalyticsFalse()
    {
        var userId = await SeedUserAsync("free");

        var body = await ReadJsonAsync(await GetAsync("/api/v1/billing/plan", userId));

        Assert.False(body.GetProperty("features").GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public async Task TenThousandEvents_TotalCorrect() // EC-023-4
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, 10_000)
            .Select(i => Event(id, now.AddMinutes(-(i % 2000)), i % 2 == 0 ? "NL" : "DE", i % 3 == 0 ? "Chrome" : "Firefox"))
            .ToArray();
        Db.Set<DownloadEvent>().AddRange(rows);
        await Db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        using var response = await AnalyticsAsync(id, userId);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10_000, (await ReadJsonAsync(response)).GetProperty("total").GetInt32());
        Console.WriteLine($"analytics over 10k events: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task DailyQuery_SeeksIxDownEventT()
    {
        var id = await SeedTransferAsync(null);
        await AddEventsAsync(Enumerable.Range(0, 50).Select(i => Event(id, DateTime.UtcNow.AddHours(-i), "NL", "Chrome")).ToArray());

        await using var ctx = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(Sql.GetConnectionString()).Options);
        var sql = TransferAnalyticsStore.DailyQuery(ctx.Set<DownloadEvent>(), id, DateTime.UtcNow.AddDays(-30)).ToQueryString();

        await using var conn = new SqlConnection(Sql.GetConnectionString());
        await conn.OpenAsync();
        await using (var on = conn.CreateCommand())
        {
            on.CommandText = "SET SHOWPLAN_XML ON";
            await on.ExecuteNonQueryAsync();
        }

        var plan = new StringBuilder();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                    plan.Append(reader.GetString(0));
            } while (await reader.NextResultAsync());
        }

        await using (var off = conn.CreateCommand())
        {
            off.CommandText = "SET SHOWPLAN_XML OFF";
            await off.ExecuteNonQueryAsync();
        }

        var xml = plan.ToString();
        Assert.Contains("Index=\"[IX_DownEvent_T]\"", xml);
        Assert.Contains("<SeekPredicates>", xml);
    }

    [Fact]
    public async Task RecordSingleFileDownload_PersistsUserAgentFamily()
    {
        var id = await SeedTransferAsync(null);

        await new PublicTransferStore(Db).RecordSingleFileDownloadAsync(id, Guid.NewGuid(), null, null, "Firefox", CancellationToken.None);

        var family = await Db.Set<DownloadEvent>().AsNoTracking().Where(e => e.TransferId == id).Select(e => e.UserAgentFamily).SingleAsync();
        Assert.Equal("Firefox", family);
    }
}

/// <summary>Global feature.analytics gate left at its seeded "false": even a Pro user gets 404 FEATURE_DISABLED (FR-023-5).</summary>
public class TransferAnalyticsEndpointTests_GateOff() : TransferAnalyticsEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task GateOff_ProUser_404FeatureDisabled()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("FEATURE_DISABLED", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }
}
