using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-008-01 (TA-4.2#8 / FR-008-3): wire-level exit checks for POST /api/v1/auth/signup —
/// AC-008-1 (200 + profile DTO + session token on new email), EC-008-1/4 (same email different case → same row, no duplicate), and the VALIDATION Problem+JSON path.
/// Fresh SQL 2022 per instance (TA-14.3); mirrors the DraftEndpointTests pattern (MigrateAsync seeds the free plan + AppUser table).
/// </summary>
public class SignupEndpointTests : IAsyncLifetime
{
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // Program.cs builds eagerly from top-level statements — env vars are the only way to inject config (DraftEndpointTests' comment).
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        // Fresh SQL + migrations: seeds the free plan row (TA-3.4) and creates dbo.AppUser (T-052).
        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    // ---- AC-008-1: new email → 200 with profile DTO + session token; DB row exists ----

    [Fact]
    public async Task Signup_NewEmail_Returns200_ProfileDto_AndSetSessionTokenValue()
    {
        const string Email = "wireuser@example.com";
        const string Password = "password123";

        using var response = await _client.PostAsJsonAsync("/api/v1/auth/signup", new { email = Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // The wire shape is pinned at the endpoint's single construction site (TA-4.2a).
        Assert.NotEmpty(root.GetProperty("sessionToken").GetString()!); // TA-9.1 session-token value in the 200 body
        Assert.Equal(Email.ToLowerInvariant(), root.GetProperty("email").GetString());
        Assert.Equal("wireuser", root.GetProperty("displayName").GetString()); // FR-008-4: local part of email

        // The row exists and is lowercased (EC-008-1) — one AppUser row for this email.
        var row = await _db.Set<AppUser>().FirstOrDefaultAsync(u => u.Email == Email.ToLowerInvariant());
        Assert.NotNull(row);
        Assert.NotEmpty(row!.PasswordHash); // PBKDF2 hashed (TA-9.1) — never the raw password
    }

    // ---- EC-008-1/4: same email, different case → signs into the same row; no duplicate ----

    [Fact]
    public async Task Signup_SameEmailDifferentCase_UpdatesSameRow_NoDuplicate()
    {
        const string Email = "dupecase@example.com";
        const string Password = "password123";

        // First signup: creates the row.
        using (var first = await _client.PostAsJsonAsync("/api/v1/auth/signup", new { email = Email.ToUpperInvariant(), password = Password }))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second signup with different case of the same email — sign-up is sign-in (FR-008-3): no "email taken" error.
        using var second = await _client.PostAsJsonAsync("/api/v1/auth/signup", new { email = Email.ToLowerInvariant(), password = Password });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsByteArrayAsync());
        Assert.NotEmpty(doc.RootElement.GetProperty("sessionToken").GetString()!); // signed in — same cookie behavior as sign-up

        // Exactly one row: no duplicate on the known-email (different-case) branch (EC-008-4).
        var count = await _db.Set<AppUser>().CountAsync(u => u.Email == Email.ToLowerInvariant());
        Assert.Equal(1, count);
    }

    // ---- VALIDATION Problem+JSON via the existing pipeline (TA-4.1.3 closed list) ----

    [Fact]
    public async Task Signup_BadEmail_Returns400_VALIDATION_ProblemJson()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/signup", new { email = "not-an-email", password = "password123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // Problem+JSON via the existing pipeline — no new code (TA-4.1.3): VALIDATION pinned at 400.
        Assert.Equal("VALIDATION", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());

        // No row written on validation failure.
        Assert.Equal(0, await _db.Set<AppUser>().CountAsync(u => u.Email == "not-an-email"));
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
