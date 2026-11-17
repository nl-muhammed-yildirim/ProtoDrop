using System.Net;
using System.Net.Http.Json;
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
/// US-019-01 (FR-019-1, AC-019-1, AC-019-5): POST /api/v1/billing/checkout with a fake IStripeCheckoutGateway (never real Stripe).
/// Fresh Azurite + SQL 2022 per instance — same pattern as StripeWebhookEndpointTests.
/// </summary>
public class CheckoutEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string AnnualPriceId = "price_test_pro_annual";
    private const string Path = "/api/v1/billing/checkout";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly FakeGateway _gateway = new();
    private Guid _userId;
    private Guid _freeId;
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

        _freeId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        var pro = await _db.Set<Plan>().FirstAsync(p => p.Code == "pro");
        _proId = pro.Id;
        var price = PlanPrice.Read(pro);
        price.Monthly.StripePriceId = "price_test_pro_monthly";
        price.Annual.StripePriceId = AnnualPriceId;
        pro.PriceJson = JsonSerializer.Serialize(price);

        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = "checkout-user@x.com",
            DisplayName = "Checkout Test",
            PlanId = _freeId,
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
        using var response = await _client.PostAsJsonAsync(Path, new { planCode = "pro", interval = "annual" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHENTICATED", await CodeOf(response));
        Assert.Empty(_gateway.Calls);
    }

    [Fact]
    public async Task Annual_Returns200WithUrl_AndPersistsNothing()
    {
        using var response = await PostAsync(new { planCode = "pro", interval = "annual" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("https://checkout.stripe.test/c/cs_fake", doc.RootElement.GetProperty("url").GetString());
        Assert.Equal("cs_fake", doc.RootElement.GetProperty("sessionId").GetString());

        var call = Assert.Single(_gateway.Calls);
        Assert.Equal(_userId, call.UserId);
        Assert.Equal(AnnualPriceId, call.PriceId);

        // Abandon case: nothing is written until a webhook arrives.
        Assert.Equal(0, await _db.Set<Subscription>().CountAsync());
        Assert.Equal(_freeId, (await _db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.Id == _userId)).PlanId);
    }

    [Fact]
    public async Task StripeOutage_503BillingUnavailable_NothingWritten_ThenRetrySucceeds()
    {
        _gateway.Failure = new BillingUnavailableException("down");
        using (var response = await PostAsync(new { planCode = "pro", interval = "monthly" }))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType); // house writer emits Problem+JSON shape as application/json
            Assert.Equal("BILLING_UNAVAILABLE", await CodeOf(response));
        }

        Assert.Equal(0, await _db.Set<Subscription>().CountAsync());
        Assert.Equal(0, await _db.Set<StripeEvent>().CountAsync());
        Assert.Equal(_freeId, (await _db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.Id == _userId)).PlanId);

        _gateway.Failure = null;
        using var retry = await PostAsync(new { planCode = "pro", interval = "monthly" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task AlreadyOnPro_400Validation_GatewayNotCalled()
    {
        var user = await _db.Set<AppUser>().SingleAsync(u => u.Id == _userId);
        user.PlanId = _proId;
        await _db.SaveChangesAsync();

        using var response = await PostAsync(new { planCode = "pro", interval = "monthly" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION", await CodeOf(response));
        Assert.Empty(_gateway.Calls);
    }

    [Fact]
    public async Task UnknownInterval_400Validation()
    {
        using var response = await PostAsync(new { planCode = "pro", interval = "weekly" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION", await CodeOf(response));
        Assert.Empty(_gateway.Calls);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString();
    }

    // TA-9.1 cookie idiom (same as AdminOverviewEmailHealthTests)
    private async Task<HttpResponseMessage> PostAsync(object body)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        return await _client.SendAsync(request);
    }

    private sealed class FakeGateway : IStripeCheckoutGateway
    {
        public List<StripeCheckoutRequest> Calls { get; } = new();
        public Exception? Failure { get; set; }

        public Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct)
        {
            Calls.Add(request);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new StripeCheckoutSession("cs_fake", "https://checkout.stripe.test/c/cs_fake"));
        }

        public Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct) =>
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
