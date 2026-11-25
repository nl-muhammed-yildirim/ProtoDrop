using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// Regression: AppUserStore is a singleton over a singleton WaDbContext. A tracked read was served from the identity map,
/// so an AppUser.PlanId written to the database out-of-band (admin set-plan, Stripe webhook, grace downgrade, direct SQL)
/// was invisible to /auth/login until the API restarted. Reads must hit the database every time.
/// </summary>
public class LoginReflectsPlanChangeTests : IAsyncLifetime
{
    private static readonly Guid ProPlanId = Guid.Parse("11111111-0000-4000-8000-000000000002");

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task Login_AfterPlanChangedInDatabase_ReturnsNewPlanCode_WithoutRestart()
    {
        const string Email = "planchange@example.com";
        const string Password = "password123";

        using (var signup = await _client.PostAsJsonAsync("/api/v1/auth/signup", new { email = Email, password = Password }))
            Assert.Equal(HttpStatusCode.OK, signup.StatusCode);

        // First login warms whatever the store would cache (the pre-fix identity map).
        Assert.Equal("free", await LoginPlanCodeAsync(Email, Password));

        var updated = await _db.Set<AppUser>().Where(u => u.Email == Email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PlanId, ProPlanId));
        Assert.Equal(1, updated);

        Assert.Equal("pro", await LoginPlanCodeAsync(Email, Password));
    }

    private async Task<string?> LoginPlanCodeAsync(string email, string password)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.GetProperty("planCode").GetString();
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
