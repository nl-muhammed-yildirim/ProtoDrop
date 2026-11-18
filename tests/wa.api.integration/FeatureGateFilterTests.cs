using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using wa.api.Middleware;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain;

namespace wa.api.integration;

/// <summary>
/// US-044-02 (FR-044-3/7, AC-044-2, EC-044-6): the RequireFeature endpoint filter on a minimal TestServer host (no Docker) —
/// gate off or absent or garbled → 404 FEATURE_DISABLED and the handler never runs; on → pass-through; one flag read per request.
/// </summary>
public class FeatureGateFilterTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Off_Returns404FeatureDisabled_HandlerNotRun()
    {
        await using var host = await StartAsync(new FeatureFlag { Key = FeatureGates.DataExport, Value = "false" });

        using var response = await host.Client.GetAsync("/gated");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FEATURE_DISABLED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Feature disabled", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, host.HandlerRuns);
    }

    [Fact]
    public async Task On_PassesThrough()
    {
        await using var host = await StartAsync(new FeatureFlag { Key = FeatureGates.DataExport, Value = "true" });

        using var response = await host.Client.GetAsync("/gated");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.HandlerRuns);
    }

    [Fact]
    public async Task AbsentRow_FailsClosed404() // EC-044-6
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/gated");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, host.HandlerRuns);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("1")]
    public async Task GarbledValue_FailsClosed404(string raw)
    {
        await using var host = await StartAsync(new FeatureFlag { Key = FeatureGates.DataExport, Value = raw });

        using var response = await host.Client.GetAsync("/gated");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, host.HandlerRuns);
    }

    [Fact]
    public async Task ResolvesOncePerRequest()
    {
        await using var host = await StartAsync(new FeatureFlag { Key = FeatureGates.DataExport, Value = "true" });

        var before = host.StoreCalls;
        using var first = await host.Client.GetAsync("/gated");
        var afterFirst = host.StoreCalls;
        using var second = await host.Client.GetAsync("/gated");

        Assert.Equal(1, afterFirst - before);
        Assert.Equal(1, host.StoreCalls - afterFirst);
    }

    [Theory]
    [InlineData("feature.resend")]
    [InlineData("feature.nope")]
    public void UnknownGateKey_AtRegistration_Throws(string key)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        Assert.Throws<ArgumentException>(() => app.MapGet("/x", () => "x").RequireFeature(key));
    }

    private static async Task<GateHost> StartAsync(params FeatureFlag[] rows)
    {
        var store = new CountingFlagsStore(rows);
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableClock(now);
        // TTL zero: every read reloads, so the store call count shows how many reads one request made.
        var provider = new LimitsProvider(new PlansCache(new NoPlansStore(), Ttl, () => clock.Now), new FlagsCache(store, TimeSpan.Zero, () => clock.Now));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IFlagResolver>(provider);
        var app = builder.Build();

        var runs = new Counter();
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseMiddleware<GlobalErrorMiddleware>();
        app.MapGet("/gated", () => { runs.Value++; return Results.Ok("ok"); }).RequireFeature(FeatureGates.DataExport);

        await app.StartAsync();
        return new GateHost(app, runs, store);
    }

    private sealed class Counter { public int Value; }

    private sealed class MutableClock(DateTimeOffset start)
    {
        private DateTimeOffset _now = start;
        // Advances 1 ms per read so a zero-TTL cache always sees its entry as expired.
        public DateTimeOffset Now => _now = _now.AddMilliseconds(1);
    }

    private sealed class GateHost(WebApplication app, Counter runs, CountingFlagsStore store) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();
        public int HandlerRuns => runs.Value;
        public int StoreCalls => store.Calls;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class CountingFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(rows);
        }
    }

    private sealed class NoPlansStore : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => throw new PlanUnknownException(planCode);
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }
}
