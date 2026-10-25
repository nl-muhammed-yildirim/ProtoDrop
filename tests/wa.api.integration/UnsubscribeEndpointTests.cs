using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-006-03 exit checks (FR-006-5, AC-006-3): GET /unsubscribe/{token} + admin suppression list/remove.
/// Valid token → 200 + EmailSuppression row exists; double-click → idempotent no-op (same landing page);
/// tampered token → 404 + warn telemetry; admin removal emits admin_action.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as UnlockEndpointTests.
/// </summary>
public class UnsubscribeEndpointTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;

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

        UnsubscribeTelemetryCapture.Lines.Clear(); // per-test isolation: assertions below cover only this test's requests (fresh host per instance)

        _factory = new TestFactory();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Exit check 1: valid token → 200 + EmailSuppression row exists for (address, sender) ----

    [Fact]
    public async Task ValidToken_Returns200_SuppressionRowExists()
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var token = UnsubscribeToken.Create("recipient@x.com", "sender@y.com", key);

        using var response = await _client.GetAsync($"/unsubscribe/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await _db.Set<EmailSuppression>().SingleAsync();
        Assert.Equal("recipient@x.com", row.Address);
        Assert.Equal("sender@y.com", row.SenderEmail);
    }

    // ---- Exit check 2: second click / stale token → no-op with same success (idempotent via UQ_ES) ----

    [Fact]
    public async Task SameTokenTwice_SecondCallStill200_OnlyOneRow()
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var token = UnsubscribeToken.Create("dup@x.com", "sender@y.com", key);

        using (var first = await _client.GetAsync($"/unsubscribe/{token}"))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using (var second = await _client.GetAsync($"/unsubscribe/{token}"))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode); // idempotent — same success landing page

        var count = await _db.Set<EmailSuppression>().CountAsync();
        Assert.Equal(1, count); // UQ_ES: only one row (SQL Server enforces the unique constraint)
    }

    // ---- Exit check 3: tampered token → 404 + warn telemetry ----

    [Fact]
    public async Task TamperedToken_Returns404_WarnTelemetry()
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var validToken = UnsubscribeToken.Create("a@b.com", "c@d.com", key);

        // Flip one character in the signature segment to create a tampered token.
        var segments = validToken.Split('.', 2);
        var sigChars = segments[1].ToCharArray();
        sigChars[^1] = sigChars[^1] == 'A' ? 'B' : 'A';
        var tampered = $"{segments[0]}.{new string(sigChars)}";

        using var response = await _client.GetAsync($"/unsubscribe/{tampered}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404 — don't leak "token invalid" detail

        // No row inserted for the tampered token.
        Assert.Equal(0, await _db.Set<EmailSuppression>().CountAsync());

        // Warn telemetry: the log line contains "Unsubscribe token verification failed".
        Assert.Contains(UnsubscribeTelemetryCapture.Lines, l => l.Contains("Unsubscribe token verification failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GarbageToken_Returns404()
    {
        using var response = await _client.GetAsync("/unsubscribe/not-a-valid-token!!!");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Exit check 4: admin list/remove + admin_action telemetry ----

    [Fact]
    public async Task AdminList_ReturnsSuppressionRows()
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var token = UnsubscribeToken.Create("admin-test@x.com", "sender@y.com", key);
        using (var unsub = await _client.GetAsync($"/unsubscribe/{token}"))
            Assert.Equal(HttpStatusCode.OK, unsub.StatusCode);

        using var response = await _client.GetAsync("/api/v1/admin/suppressions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var arr = doc.RootElement;
        Assert.True(arr.GetArrayLength() >= 1);

        // Find our row.
        string? foundId = null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.GetProperty("address").GetString() == "admin-test@x.com" &&
                item.GetProperty("senderEmail").GetString() == "sender@y.com")
            {
                foundId = item.GetProperty("id").GetString();
                break;
            }
        }
        Assert.NotNull(foundId);
    }

    [Fact]
    public async Task AdminDelete_RemovesRow_EmitsAdminActionTelemetry()
    {
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var token = UnsubscribeToken.Create("del@x.com", "sender@y.com", key);
        using (var unsub = await _client.GetAsync($"/unsubscribe/{token}"))
            Assert.Equal(HttpStatusCode.OK, unsub.StatusCode);

        // Get the row id.
        var row = await _db.Set<EmailSuppression>().FirstAsync(s => s.Address == "del@x.com");

        UnsubscribeTelemetryCapture.Lines.Clear(); // isolate this test's telemetry

        using (var del = await _client.DeleteAsync($"/api/v1/admin/suppressions/{row.Id:N}"))
            Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        // Row is gone.
        var count = await _db.Set<EmailSuppression>().CountAsync(s => s.Address == "del@x.com");
        Assert.Equal(0, count);

        // admin_action telemetry emitted.
        Assert.Contains(UnsubscribeTelemetryCapture.Lines, l => l.Contains(TelemetryEvents.AdminAction, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdminDelete_UnknownId_Returns404()
    {
        using var response = await _client.DeleteAsync($"/api/v1/admin/suppressions/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Test factory: swap the telemetry logger so we can inspect rendered log lines ----

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILogger<WaTelemetryReporter>>();
                services.AddSingleton<ILogger<WaTelemetryReporter>>(_ => new CaptureLogger());
                services.RemoveAll<WaTelemetryReporter>(); // Program.cs's singleton was already registered — this later registration wins for all consumers
                services.AddSingleton<wa.application.Telemetry.WaTelemetryReporter>(sp =>
                    new WaTelemetryReporter(
                        sp.GetRequiredService<ILogger<WaTelemetryReporter>>(),
                        System.Text.Encoding.UTF8.GetBytes(TestSecret))); // same key bytes as Program.cs — TA-9.4: the reporter is config-free
            });
        }
    }

    /// <summary>Captures every rendered log line into the shared list (warn telemetry assertions read it).</summary>
    private sealed class CaptureLogger : ILogger<WaTelemetryReporter>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (!string.IsNullOrEmpty(message))
                lock (typeof(CaptureLogger))
                    UnsubscribeTelemetryCapture.Lines.Add(message);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

// Shared across the factory host + tests (static — WebApplicationFactory caches one host per process).
public static class UnsubscribeTelemetryCapture
{
    public static readonly List<string> Lines = new();
}
