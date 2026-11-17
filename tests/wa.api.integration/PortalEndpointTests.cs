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
using wa.application.UseCases.Billing;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-019-02 (AC-019-1 portal half, EC no-subscription fallback): GET /api/v1/billing/portal with a fake IStripeCheckoutGateway
/// (never real Stripe). Fresh Azurite + SQL 2022 per instance — same pattern as CheckoutEndpointTests.
/// </summary>
public class PortalEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string Path = "/api/v1/billing/portal";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly FakeGateway _gateway = new();
    private Guid _userId;
    private Guid _proId;

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

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        var freeId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _proId = await _db.Set<Plan>().Where(p => p.Code == "pro").Select(p => p.Id).FirstAsync();

        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = "portal-user@x.com",
            DisplayName = "Portal Test",
            PlanId = freeId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _factory = new TestFactory(_gateway);
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
        Assert.Equal("UNAUTHENTICATED", await CodeOf(response));
        Assert.Empty(_gateway.Calls);
    }

    [Fact]
    public async Task WithSubscription_200PortalUrl_AndPersistsNothing()
    {
        await SeedSubscriptionAsync();

        using var response = await GetAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("https://billing.stripe.test/p/bps_fake", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("fallback").ValueKind);

        var call = Assert.Single(_gateway.Calls);
        Assert.Equal("cus_portal_1", call.CustomerId);

        // Read-only endpoint.
        Assert.Equal(1, await _db.Set<Subscription>().CountAsync());
        Assert.Equal(0, await _db.Set<StripeEvent>().CountAsync());
    }

    [Fact]
    public async Task NoSubscription_200UpgradeFallback_GatewayNotCalled()
    {
        using var response = await GetAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("url").ValueKind);
        Assert.Equal("upgrade", doc.RootElement.GetProperty("fallback").GetString());
        Assert.Empty(_gateway.Calls);
    }

    [Fact]
    public async Task StripeOutage_503BillingUnavailable()
    {
        await SeedSubscriptionAsync();
        _gateway.Failure = new BillingUnavailableException("down");

        using var response = await GetAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("BILLING_UNAVAILABLE", await CodeOf(response));
    }

    private async Task SeedSubscriptionAsync()
    {
        var now = DateTime.UtcNow;
        var user = await _db.Set<AppUser>().SingleAsync(u => u.Id == _userId);
        user.PlanId = _proId;
        _db.Set<Subscription>().Add(new Subscription
        {
            Id = Guid.NewGuid(),
            AppUserId = _userId,
            StripeSubId = "sub_portal_1",
            StripeCustomerId = "cus_portal_1",
            PlanCode = "pro",
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _db.SaveChangesAsync();
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    // TA-9.1 cookie idiom (same as CheckoutEndpointTests)
    private async Task<HttpResponseMessage> GetAsync()
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        return await _client.SendAsync(request);
    }

    private sealed class FakeGateway : IStripeCheckoutGateway
    {
        public List<(string CustomerId, string ReturnUrl)> Calls { get; } = new();
        public Exception? Failure { get; set; }

        public Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct)
        {
            Calls.Add((customerId, returnUrl));
            if (Failure is not null) throw Failure;
            return Task.FromResult(new StripePortalSession("bps_fake", "https://billing.stripe.test/p/bps_fake"));
        }

        public Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly IStripeCheckoutGateway _gateway;
        public TestFactory(IStripeCheckoutGateway gateway) => _gateway = gateway;

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStripeCheckoutGateway>();
                services.AddSingleton(_gateway);
            });
    }
}
