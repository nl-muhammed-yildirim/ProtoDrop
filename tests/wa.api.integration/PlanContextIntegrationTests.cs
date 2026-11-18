using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-020-01 (FR-020-1, FR-020-4, EC-020-3): a capturing MediatR pipeline behavior reads IPlanContextAccessor exactly like a handler would.
/// Fresh Azurite + SQL 2022 per instance — same pattern as CheckoutEndpointTests.
/// </summary>
public class PlanContextIntegrationTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private readonly PlanSink _sink = new();
    private Guid _proUserId;

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

        var proPlanId = await _db.Set<Plan>().Where(p => p.Code == "pro").Select(p => p.Id).FirstAsync();
        _proUserId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _proUserId,
            Email = "plan-context-user@x.com",
            DisplayName = "Plan Context",
            PlanId = proPlanId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _factory = new TestFactory(_sink);
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
    public async Task AuthenticatedProUser_HandlerSeesProPlanContext()
    {
        using var response = await GetMeAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var captured = _sink.For("GetProfileCommand");
        Assert.Equal("pro", captured.PlanCode);

        var expected = await _factory.Services.GetRequiredService<ILimitsProvider>().Resolve("pro");
        foreach (var property in typeof(LimitsRecord).GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            Assert.Equal(property.GetValue(expected), property.GetValue(captured.Limits));
        // Seeded feature.* gates default to false (TA-13.2 kill-switch precedence), so compare to the resolver rather than the raw plan row.
        var expectedFeatures = await _factory.Services.GetRequiredService<IFeaturesProvider>().Resolve("pro");
        foreach (var property in typeof(PlanFeatures).GetProperties())
            Assert.Equal(property.GetValue(expectedFeatures), property.GetValue(captured.Features));
    }

    [Fact]
    public async Task Guest_HandlerSeesFree()
    {
        using var response = await _client.GetAsync("/health");
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);

        Assert.Equal(Plan.DefaultCode, _sink.For("HealthQuery").PlanCode);
    }

    [Fact]
    public async Task SoftDeletedProUser_ResolvesLastKnownPlan()
    {
        var user = await _db.Set<AppUser>().SingleAsync(u => u.Id == _proUserId);
        user.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        using var response = await GetMeAsync();
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // existing behavior for a soft-deleted profile

        Assert.Equal("pro", _sink.For("GetProfileCommand").PlanCode);
    }

    // TA-9.1 cookie idiom (same as CheckoutEndpointTests)
    private async Task<HttpResponseMessage> GetMeAsync()
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_proUserId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        return await _client.SendAsync(request);
    }

    private sealed class PlanSink
    {
        private readonly ConcurrentDictionary<string, PlanContext> _byRequest = new();

        public void Record(string requestName, PlanContext context) => _byRequest[requestName] = context;

        public PlanContext For(string requestName) =>
            _byRequest.TryGetValue(requestName, out var context)
                ? context
                : throw new InvalidOperationException($"No PlanContext captured for {requestName}.");
    }

    private sealed class CapturePlanBehavior<TRequest, TResponse>(IPlanContextAccessor accessor, PlanSink sink)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            sink.Record(typeof(TRequest).Name, await accessor.GetAsync(cancellationToken));
            return await next();
        }
    }

    private sealed class TestFactory(PlanSink sink) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(sink);
                services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CapturePlanBehavior<,>));
            });
    }
}
