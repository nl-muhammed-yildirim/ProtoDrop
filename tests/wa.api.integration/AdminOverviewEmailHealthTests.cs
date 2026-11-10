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
/// T-013-05 (AC-013-3 / FR-013-4): admin health surface — with Hub down (dummy Service Bus connection),
/// GET /api/v1/admin/overview shows non-zero emailPendingCount while the sender saw nothing.
/// The f-email worker does NOT run in api.integration tests, so emailFailedTotal/dlqCount are 0 and
/// lastSuccessfulDeliveryAtUtc is null by design — PENDING COUNT is the only reliable non-zero signal.
/// </summary>
public class AdminOverviewEmailHealthTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    // T-011-01 gate: seed this user into Wa__AdminEmails (SuperAdmin) and sign requests with its TA-9.1 session cookie.
    private const string AdminEmail = "health-admin@x.com";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _adminUserId = default;

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
        // Program.cs reads Wa:AdminEmails at host build — must be set before the first CreateClient below (TA-9.2 gate).
        Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail);
        // Hub down: dummy Service Bus connection — same pattern as SendEndpointTests L45.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=***");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        // T-011-01: the gate resolves role from AppUser.Email against the admin set — seed a SuperAdmin row.
        var planId = await _db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();
        _adminUserId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _adminUserId,
            Email = AdminEmail.ToLowerInvariant(),
            DisplayName = "Health Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _factory = new TestFactory();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- AC-013-3: admin Overview shows non-zero pending/failed counts while the sender saw nothing (FR-013-4) ----

    [Fact]
    public async Task AdminOverview_WithHubDown_ShowsNonZeroPendingCount_AllFourFieldsPresent()
    {
        // Seed pending recipients directly — NotifiedAtUtc null = never notified (the f-email worker doesn't run here).
        // FK_ER_T: EmailRecipient.TransferId must reference a real Transfer row.
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "TESTLINK",
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
            MaxDownloads = 5,
            SenderName = "Health Test",
            TotalBytes = 0,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.Set<EmailRecipient>().AddRange(
            new EmailRecipient { Id = Guid.NewGuid(), TransferId = transferId, Address = "pending1@x.com" },
            new EmailRecipient { Id = Guid.NewGuid(), TransferId = transferId, Address = "pending2@x.com" });
        await _db.SaveChangesAsync();

        // GET /api/v1/admin/overview — sign in as the seeded SuperAdmin (TA-9.1 cookie idiom).
        using var response = await WithSessionCookieAsync(HttpMethod.Get, "/api/v1/admin/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // PENDING COUNT is the reliable non-zero signal in this environment (FR-013-4).
        Assert.True(root.GetProperty("emailPendingCount").GetInt32() > 0);

        // The other three fields are present but 0/null by design: the worker doesn't run and the SB client
        // can't reach a real DLQ with a dummy connection — that's expected, not a failure.
        Assert.True(root.TryGetProperty("emailFailedTotal", out _));
        Assert.True(root.TryGetProperty("lastSuccessfulDeliveryAge", out _));
        Assert.True(root.TryGetProperty("dlqCount", out _));
    }

    // ---- TA-9.1 cookie idiom (same as UnsubscribeEndpointTests) ----

    private async Task<HttpResponseMessage> WithSessionCookieAsync(HttpMethod method, string url)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_adminUserId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        var cookieValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"wa.session={cookieValue}");
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
