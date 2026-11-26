using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-004 US-024-04 (FR-024-3, AC-024-4, EC-024-4): the operator chain end to end — PUT /api/v1/admin/flags/feature.ads changes
/// adsEnabled on GET /api/v1/public/transfers/{linkId} for free senders only, after the real 30 s flag cache, audit-logged
/// (flag.set row + one admin.action envelope); invalid values are rejected and the old value stays live.
/// The Free plan row carries ads:true from the AddFreeAdsEnabled seed migration.
/// </summary>
public class AdsFlagToggleTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string AdminEmail = "ads-flag-admin@x.com";
    private const string FlagUrl = "/api/v1/admin/flags/" + FeatureGates.Ads;

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory? _factory;
    private HttpClient? _client;
    private Guid _adminUserId;

    private RecordingPublisher Publisher => EnsureClient().Publisher;

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
        // Role derives per request from Wa:AdminEmails — must be set before the host builds.
        Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail);

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        var planId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _adminUserId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _adminUserId,
            Email = AdminEmail.ToLowerInvariant(),
            DisplayName = "Ads Flag Test",
            PlanId = planId,
            EmailConfirmed = true, // the admin role needs a confirmed email (CHORE-staging-blockers)
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

    [Fact]
    public async Task FlagOff_NoAdsOnAnyPlan() // AC-024-4
    {
        EnsureClient();
        await SeedTransferAsync(await SeedOwnerAsync(Plan.DefaultCode), "a1a1a1a1");
        await SeedTransferAsync(await SeedOwnerAsync("pro"), "a2a2a2a2");
        await SeedTransferAsync(await SeedOwnerAsync("business"), "a3a3a3a3");
        await SeedTransferAsync(null, "a4a4a4a4");

        Assert.Equal("false", await StoredFlagAsync());
        Assert.False(await GetAdsEnabledAsync("a1a1a1a1"));
        Assert.False(await GetAdsEnabledAsync("a2a2a2a2"));
        Assert.False(await GetAdsEnabledAsync("a3a3a3a3"));
        Assert.False(await GetAdsEnabledAsync("a4a4a4a4"));
    }

    [Fact]
    public async Task OperatorSetsFlagTrue_AfterTtl_FreeShowsAds_ProStillNone_AuditLogged() // FR-024-3, EC-024-4
    {
        EnsureClient();
        await SeedTransferAsync(await SeedOwnerAsync(Plan.DefaultCode), "b1b1b1b1");
        await SeedTransferAsync(await SeedOwnerAsync("pro"), "b2b2b2b2");

        Assert.False(await GetAdsEnabledAsync("b1b1b1b1")); // warms FlagsCache

        using (var put = await SendAsync(HttpMethod.Put, FlagUrl, "true"))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            using var doc = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
            Assert.Equal(FeatureGates.Ads, doc.RootElement.GetProperty("key").GetString());
            Assert.Equal("true", doc.RootElement.GetProperty("value").GetString());
        }

        Assert.Equal("true", await StoredFlagAsync());
        Assert.False(await GetAdsEnabledAsync("b1b1b1b1")); // still cached within the 30 s TTL

        var row = Assert.Single(await _db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == "flag.set").ToListAsync());
        Assert.Equal(FeatureGates.Ads, row.EntityId);
        Assert.Equal(AdminEmail.ToLowerInvariant(), row.ActorEmail?.ToLowerInvariant());
        using (var details = JsonDocument.Parse(row.DetailsJson!))
        {
            Assert.Equal("false", details.RootElement.GetProperty("oldValue").GetString());
            Assert.Equal("true", details.RootElement.GetProperty("newValue").GetString());
        }

        var envelope = Assert.Single(Publisher.Published, e => e.Type == EventType.AdminAction);
        Assert.Equal(FeatureGates.Ads, envelope.PartitionKey);

        // Real TTL (TA-3.4 / EC-024-4) — same approach as AdminFlagsEndpointTests; do not shorten.
        await Task.Delay(31_000);

        Assert.True(await GetAdsEnabledAsync("b1b1b1b1"));
        Assert.False(await GetAdsEnabledAsync("b2b2b2b2")); // AC-024-2 holds under the master switch
    }

    [Fact]
    public async Task KillSwitch_SetFalse_AfterTtl_FreeAdsCollapse() // EC-024-4
    {
        // Flag on in the DB before the host's first flag read.
        var flag = await _db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Ads);
        flag.Value = "true";
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        EnsureClient();
        await SeedTransferAsync(await SeedOwnerAsync(Plan.DefaultCode), "c1c1c1c1");

        Assert.True(await GetAdsEnabledAsync("c1c1c1c1"));

        using (var put = await SendAsync(HttpMethod.Put, FlagUrl, "false"))
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await Task.Delay(31_000);

        Assert.False(await GetAdsEnabledAsync("c1c1c1c1"));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("True")]
    [InlineData("{\"on\":true}")]
    public async Task InvalidValue_Rejected_OldValueStays_NoAudit_NoEvent_AdsUnchanged(string body)
    {
        EnsureClient();
        await SeedTransferAsync(await SeedOwnerAsync(Plan.DefaultCode), "d1d1d1d1");

        using var put = await SendAsync(HttpMethod.Put, FlagUrl, body);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        using (var doc = JsonDocument.Parse(await put.Content.ReadAsStringAsync()))
            Assert.Equal("FLAG_INVALID", doc.RootElement.GetProperty("code").GetString());

        Assert.Equal("false", await StoredFlagAsync());
        Assert.Empty(await _db.Set<AuditLog>().AsNoTracking().Where(a => a.Action == "flag.set").ToListAsync());
        Assert.DoesNotContain(Publisher.Published, e => e.Type == EventType.AdminAction);
        Assert.False(await GetAdsEnabledAsync("d1d1d1d1"));
    }

    private TestFactory EnsureClient()
    {
        if (_factory is null)
        {
            _factory = new TestFactory();
            _client = _factory.CreateClient();
        }
        return _factory;
    }

    private async Task<string> StoredFlagAsync() =>
        (await _db.Set<FeatureFlag>().AsNoTracking().FirstAsync(f => f.Key == FeatureGates.Ads)).Value;

    private async Task<bool> GetAdsEnabledAsync(string linkId)
    {
        using var response = await _client!.GetAsync($"/api/v1/public/transfers/{linkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.GetProperty("adsEnabled").GetBoolean();
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

    private async Task SeedTransferAsync(Guid? ownerId, string linkId)
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

    // TA-9.1 session cookie forged with the test secret (same idiom as AdminFlagsEndpointTests).
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
        return await _client!.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        public RecordingPublisher Publisher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventPublisher>();
                services.AddSingleton<IEventPublisher>(Publisher);
            });
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            lock (Published) Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }
}
