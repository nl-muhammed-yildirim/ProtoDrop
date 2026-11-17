using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Ports;
using wa.application.UseCases.Billing;
using wa.domain;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-019-03 (FR-019-4/5, AC-019-3, AC-019-4, EC-019-1, EC-019-6): POST /api/v1/billing/webhooks/stripe.
/// Signature gate, event-id dedup (sequential + concurrent), unknown-type no-op, unresolved user stays retryable.
/// Fresh Azurite + SQL 2022 per instance — same pattern as UnsubscribeEndpointTests.
/// </summary>
public class StripeWebhookEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string TestWebhookSecret = "whsec_integration_test";
    private const string ProPriceId = "price_test_pro";
    private const string Path = "/api/v1/billing/webhooks/stripe";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
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
        Environment.SetEnvironmentVariable("Wa__Stripe__WebhookSecret", TestWebhookSecret);

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
        price.Monthly.StripePriceId = ProPriceId;
        pro.PriceJson = JsonSerializer.Serialize(price);

        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = "billing-user@x.com",
            DisplayName = "Billing Test",
            PlanId = _freeId,
            Theme = "system",
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

    // ---- Happy path ----

    [Fact]
    public async Task SignedSubscriptionCreated_AppliesMirror_AndPublishesPlanChanged()
    {
        using var response = await PostSignedAsync(Body("evt_happy", "customer.subscription.created", "active", _userId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sub = await _db.Set<Subscription>().AsNoTracking().SingleAsync();
        Assert.Equal("sub_1", sub.StripeSubId);
        Assert.Equal("active", sub.Status);
        Assert.Equal("pro", sub.PlanCode);
        Assert.Equal(_userId, sub.AppUserId);
        Assert.Equal(_proId, (await _db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.Id == _userId)).PlanId);
        Assert.Equal(1, await _db.Set<StripeEvent>().CountAsync());
        Assert.Equal(1, PlanChangedCount());
    }

    // ---- AC-019-3: sequential duplicate ----

    [Fact]
    public async Task SameEventTwiceSequentially_AppliedOnce()
    {
        var body = Body("evt_dup", "customer.subscription.created", "active", _userId);

        using (var first = await PostSignedAsync(body))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var updatedAfterFirst = (await _db.Set<Subscription>().AsNoTracking().SingleAsync()).UpdatedAtUtc;

        using (var second = await PostSignedAsync(body))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(1, await _db.Set<StripeEvent>().CountAsync());
        var sub = await _db.Set<Subscription>().AsNoTracking().SingleAsync();
        Assert.Equal(updatedAfterFirst, sub.UpdatedAtUtc);
        Assert.Equal(1, PlanChangedCount());
    }

    // ---- EC-019-6: concurrent duplicate ----

    [Fact]
    public async Task SameEventConcurrently_AppliedOnce_BothReturn200()
    {
        var body = Body("evt_race", "customer.subscription.created", "active", _userId);

        var responses = await Task.WhenAll(PostSignedAsync(body), PostSignedAsync(body));
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            r.Dispose();
        }

        Assert.Equal(1, await _db.Set<StripeEvent>().CountAsync());
        Assert.Equal(1, await _db.Set<Subscription>().CountAsync());
        Assert.Equal(1, PlanChangedCount());
    }

    // ---- AC-019-4: unknown type ----

    [Fact]
    public async Task UnknownEventType_Returns200_NoMirrorChange_Logged()
    {
        using var response = await PostSignedAsync(Body("evt_unknown", "customer.created", "active", _userId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(0, await _db.Set<StripeEvent>().CountAsync());
        Assert.Equal(0, await _db.Set<Subscription>().CountAsync());
        Assert.Equal(_freeId, (await _db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.Id == _userId)).PlanId);
        Assert.Contains(HandlerLogCapture.Lines, l => l.Contains("evt_unknown", StringComparison.Ordinal));
    }

    // ---- Signature ----

    [Fact]
    public async Task MissingSignature_Returns400_StripeWebhookMismatch_NothingWritten()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(Body("evt_nosig", "customer.subscription.created", "active", _userId), Encoding.UTF8, "application/json")
        };
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("STRIPE_WEBHOOK_MISMATCH", await response.Content.ReadAsStringAsync());
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task AlteredBody_Returns400_StripeWebhookMismatch_NothingWritten()
    {
        var body = Body("evt_tamper", "customer.subscription.created", "active", _userId);
        var header = SignatureHeader(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body.Replace("sub_1", "sub_2"), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", header);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("STRIPE_WEBHOOK_MISMATCH", await response.Content.ReadAsStringAsync());
        await AssertNothingWrittenAsync();
    }

    // ---- EC-019-1 shape: unresolved user stays retryable ----

    [Fact]
    public async Task UnknownUser_Returns500_NoStripeEventRow()
    {
        using var response = await PostSignedAsync(Body("evt_nouser", "customer.subscription.created", "active", Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("INTERNAL", await response.Content.ReadAsStringAsync());
        await AssertNothingWrittenAsync();
    }

    // ---- helpers ----

    private async Task AssertNothingWrittenAsync()
    {
        Assert.Equal(0, await _db.Set<StripeEvent>().CountAsync());
        Assert.Equal(0, await _db.Set<Subscription>().CountAsync());
    }

    private int PlanChangedCount() =>
        _factory.Publisher.GetByTopic("core").Count(e => e.Json.Contains("\"plan.changed\"", StringComparison.Ordinal));

    private static string Body(string eventId, string type, string status, Guid userId)
    {
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        return JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            data = new
            {
                @object = new
                {
                    id = "sub_1",
                    customer = "cus_1",
                    status,
                    current_period_end = periodEnd,
                    metadata = new { userId = userId.ToString() },
                    items = new { data = new[] { new { price = new { id = ProPriceId } } } }
                }
            }
        });
    }

    private static string SignatureHeader(string body)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestWebhookSecret));
        var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant();
        return $"t={t},v1={sig}";
    }

    private Task<HttpResponseMessage> PostSignedAsync(string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", SignatureHeader(body));
        return _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        public InMemoryEventPublisher Publisher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventPublisher>();
                services.AddSingleton<IEventPublisher>(Publisher);
                services.RemoveAll<ILogger<HandleStripeWebhookHandler>>();
                services.AddSingleton<ILogger<HandleStripeWebhookHandler>>(_ => new HandlerLogCapture());
            });
        }
    }

    private sealed class HandlerLogCapture : ILogger<HandleStripeWebhookHandler>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (!string.IsNullOrEmpty(message))
                lock (Lines)
                    Lines.Add(message);
        }

        public static readonly List<string> Lines = new();

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
