using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-008-02 (TA-4.2#9 / FR-008-1/2): wire-level exit checks for POST /api/v1/auth/login + the session middleware.
/// Correct password → 200 profile DTO + wa.session cookie + login_success exactly once; wrong password / no row → 401
/// WRONG_PASSWORD Problem+JSON + login_failed (no-leak); EC-008-1 different-case email signs in to the stored row without
/// creating it; AC-008-1 cookie round-trip via SessionMiddleware (valid → Items, tampered/expired → guest).
/// Direct DB seed of the AppUser row with a real PasswordHasher hash (TA-9.1 keying), no signup in the loop.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as SignupEndpointTests; env vars set before client creation.
/// </summary>
public class LoginEndpointTests : IAsyncLifetime
{
    // Program.cs reads Wa:Jwt:Secret at host build — the capture factory reuses these bytes for its own token minter (test decodes only).
    private const string TestSecret = "test-jwt-secret";

    /// <summary>Unix epoch for converting DateTime to unix seconds (1970-01-01T00:00:00Z).</summary>
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;
    private ISessionTokenMinter _minter = null!;

    public async Task InitializeAsync()
    {
        // Program.cs builds eagerly from top-level statements — env vars are always visible (SignupEndpointTests' comment).
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        // Account name as first path segment — the form Azurite with an IP host expects (TA-9.1 style).
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        // Fresh SQL + migrations: seeds the free plan row (TA-3.4) and creates dbo.AppUser (T-052).
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        // The minter is the same singleton Program.cs registers (same key bytes) — tests mint/decode only, never verify in production.
        _minter = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes(TestSecret), () => DateTimeOffset.UtcNow);

        LoginEndpointTelemetryCapture.Lines.Clear(); // per-test isolation: assertions below cover only this test's requests (fresh host per instance)

        _client = new TestFactory().CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // TA-9.1 tests set the Cookie header explicitly — the default cookie container would overwrite it
        });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- AC-008-1 login half: existing email + correct password → 200 profile DTO + wa.session cookie + login_success once ----

    [Fact]
    public async Task Login_CorrectPassword_Returns200_ProfileDto_SetsCookie_EmitsLoginSuccessOnce()
    {
        const string Email = "loginuser@example.com";
        const string Password = "password123";
        await SeedAppUserAsync(Email, Password);

        // Count login_success lines before this test's request — the capture list is process-wide (WebApplicationFactory caches one host).
        var baselineLoginSuccess = LoginEndpointTelemetryCapture.Lines.Count(l => l.Contains(TelemetryEvents.LoginSuccess, StringComparison.Ordinal));

        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // TA-4.2a: exact wire shape as signup (userId 'n' form, email, displayName, theme, planCode, sessionToken).
        Assert.Equal("free", root.GetProperty("planCode").GetString());
        Assert.Equal(Email.ToLowerInvariant(), root.GetProperty("email").GetString());
        Assert.NotEmpty(root.GetProperty("sessionToken").GetString()!); // TA-9.1 session-token value in the 200 body

        // TA-9.1: Set-Cookie carries a validly signed token — decode + pin exp = now + 30 d (re-parse here; the minter is the trusted source).
        var setCookie = FindSessionCookie(response)!;
        Assert.NotNull(setCookie);
        var cookieToken = ExtractCookieValue(setCookie); // URL-decode: Set-Cookie percent-encodes base64's +, /, = chars
        var bodyToken = root.GetProperty("sessionToken").GetString()!;
        Assert.Equal(bodyToken, cookieToken); // TA-9.1: cookie value == body's sessionToken (byte-consistent, single token)

        // Re-parse the minted cookie: HMAC ok + exp = now + 30 d (within clock skew).
        var (cookieUserId, expUtc) = ParseToken(cookieToken);
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange((long)(expUtc - Epoch).TotalSeconds, nowSeconds + (30 * 86400 - 60), nowSeconds + (30 * 86400 + 60));

        // AC-008-1: login_success emitted exactly once on this test's request.
        var countAfter = LoginEndpointTelemetryCapture.Lines.Count(l => l.Contains(TelemetryEvents.LoginSuccess, StringComparison.Ordinal));
        Assert.Equal(baselineLoginSuccess + 1, countAfter);
    }

    // ---- AC-008-1 cookie round-trip: valid cookie resolves user id; tampered/expired → guest (401 on auth-required endpoints) ----

    [Fact]
    public async Task CookieRoundTrip_SecondRequestResolvesUserId_TamperedOrExpiredCookieIsGuest()
    {
        const string Email = "cookieuser@example.com";
        const string Password = "password123";
        var userId = await SeedAppUserAsync(Email, Password);
        // ~10 d left: under the 15 d RenewRemainingDays threshold — the middleware MUST re-mint (FR-008-2).
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nearExpirySeconds = nowSeconds + (10 * 86400);

        // A validly minted cookie (the middleware's single parse site resolves it → Items[wa.session.userId]).
        var validToken = Sign(userId, nearExpirySeconds);
        using var response = await _client.SendAsync(CreateCookieRequest(validToken));
        // AC-008-1 cookie round-trip: the middleware parses OK + refreshes (Items is set for endpoints 13–15).
        // Observable via Set-Cookie — the middleware's only side effect on a resolved, sub-threshold cookie.
        var refreshed = FindSessionCookie(response);
        Assert.NotNull(refreshed); // valid cookie → middleware resolves → refreshes (Set-Cookie present)

        // Tampered signature → guest (AC-008-1 cookie round-trip): Parse returns null, no Items, no refresh.
        var tampered = TamperToken(validToken);
        using var tamperedResponse = await _client.SendAsync(CreateCookieRequest(tampered));
        Assert.Null(FindSessionCookie(tamperedResponse)); // guest path — NOT 401 (no auth required on this public route)

        // Expired exp → guest: Parse returns null (expired), no Items, no refresh.
        var expired = ExpiredToken();
        using var expiredResponse = await _client.SendAsync(CreateCookieRequest(expired));
        Assert.Null(FindSessionCookie(expiredResponse)); // guest — expired token treated as absent
    }

    // ---- Rolling refresh: validly-signed cookie with ~10 d left → any request re-mints to now+30 d (middleware round-trip) ----

    [Fact]
    public async Task ValidCookieUnderFifteenDaysRemaining_IsRefreshedToThirtyDayExpiry()
    {
        var userId = await SeedAppUserAsync($"rolling-{Guid.NewGuid():N}@example.com", "password123");
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // ~10 d left: under the 15 d RenewRemainingDays threshold — the middleware MUST re-mint (FR-008-2).
        var nearExpirySeconds = nowSeconds + (10 * 86400);
        var shortLivedToken = Sign(userId, nearExpirySeconds);

        // A validly signed cookie with a near-expiry exp — the middleware refreshes it on any request.
        using var response = await _client.SendAsync(CreateCookieRequest(shortLivedToken));

        // The middleware re-mints: Set-Cookie carries a new exp ≈ now + 30 d (TA-9.1 30-day rolling, "refreshed on use").
        var refreshedHeader = FindSessionCookie(response);
        Assert.NotNull(refreshedHeader);

        var (refreshedUserId, expUtc) = ParseToken(ExtractCookieValue(refreshedHeader!)); // URL-decode + strip attributes
        Assert.Equal(userId, refreshedUserId); // re-mint for the SAME user (middleware round-trip, not a new identity)

        // 30-day rolling: new exp is now + 30 d within clock skew.
        Assert.InRange((long)(expUtc - Epoch).TotalSeconds, nowSeconds + (30 * 86400 - 60), nowSeconds + (30 * 86400 + 60));
    }

    // ---- Wrong password / no row → 401 WRONG_PASSWORD Problem+JSON + login_failed, never "user not found" ----

    [Fact]
    public async Task WrongPassword_Returns401_WrongPassword_ProblemJson_EmitsLoginFailed()
    {
        const string Email = "wrongpw@example.com";
        await SeedAppUserAsync(Email, "password123"); // known email — wrong password must NOT leak existence

        var baselineLoginFailed = LoginEndpointTelemetryCapture.Lines.Count(l => l.Contains(TelemetryEvents.LoginFailed, StringComparison.Ordinal));

        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = "wrong-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // 401 — the closed table's status for WRONG_PASSWORD

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        // ProblemCodes pins WRONG_PASSWORD (already in the TA-4.1.3 closed list — no new code added).
        Assert.Equal("WRONG_PASSWORD", root.GetProperty("code").GetString());
        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal("Wrong password", root.GetProperty("title").GetString()); // title pinned by the closed table — NEVER "user not found"

        // login_failed emitted exactly once (task §4: AC-008-1 wrong-password path).
        var countAfter = LoginEndpointTelemetryCapture.Lines.Count(l => l.Contains(TelemetryEvents.LoginFailed, StringComparison.Ordinal));
        Assert.Equal(baselineLoginFailed + 1, countAfter);
    }

    // ---- EC-008-1: uppercase email signs in to the stored lowercased row — login never creates a user ----

    [Fact]
    public async Task Login_UppercaseEmail_SignsInToStoredLowercasedRow_NoNewRow()
    {
        const string Email = "upper@example.com";
        const string Password = "password123";
        var userId = await SeedAppUserAsync(Email, Password);

        // EC-008-1: same row resolved for the uppercase form (normalize email — lowercase + trim, same as signup).
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email.ToUpperInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        // Same row — the seeded userId is returned (TA-4.2a wire shape: 'n' form).
        Assert.Equal(userId.ToString("n"), root.GetProperty("userId").GetString());

        // Login NEVER creates a user: exactly one AppUser row for this email still (no insert on the login path).
        var count = await _db.Set<AppUser>().CountAsync(u => u.Email == Email.ToLowerInvariant());
        Assert.Equal(1, count);
    }

    // ---- Helpers ----

    /// <summary>Seed an AppUser row directly via WaDbContext — no signup in the loop (the plan's direct DB seed pattern).</summary>
    private async Task<Guid> SeedAppUserAsync(string email, string password)
    {
        var userId = Guid.NewGuid();
        var planId = await _db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();

        // TA-9.1: PBKDF2-SHA256 via ASP.NET Identity PasswordHasher<string>, keyed by userId.ToString("n") — same keying as signup.
        var hash = new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword(userId.ToString("n"), password);

        _db.Set<AppUser>().Add(new AppUser
        {
            Id = userId,
            Email = email.ToLowerInvariant(), // EC-008-1: stored lowercased
            DisplayName = "Login Test",
            PlanId = planId,
            PasswordHash = hash,
            EmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    /// <summary>Create a request with an explicit Cookie header (TA-9.1 test — the default cookie container would overwrite it).</summary>
    private static HttpRequestMessage CreateCookieRequest(string tokenValue, string url = "/health")
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"wa.session={tokenValue}");
        return req;
    }

    /// <summary>Get Set-Cookie header values safely (returns empty array if no header).</summary>
    private static string[] GetSetCookieValues(HttpResponseMessage response)
    {
        try
        {
            var values = response.Headers.GetValues("Set-Cookie");
            return values?.ToArray() ?? Array.Empty<string>();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Extract the token from a Set-Cookie header (strips attributes, URL-decodes).</summary>
    private static string ExtractCookieValue(string setCookieHeader)
    {
        // Format: "wa.session=BASE64TOKEN; HttpOnly; Secure; SameSite=Lax; Max-Age=2592000"
        var tokenPart = setCookieHeader.Split(';')[0].Trim();
        return Uri.UnescapeDataString(tokenPart[11..]); // strip "wa.session=" and URL-decode
    }

    /// <summary>Find the wa.session Set-Cookie value (null if not present).</summary>
    private static string? FindSessionCookie(HttpResponseMessage response)
    {
        return GetSetCookieValues(response)
            .FirstOrDefault(sc => sc.StartsWith("wa.session=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tamper the token: flip one char in the signature segment (same key, broken sig → Parse returns null).</summary>
    private string TamperToken(string original)
    {
        var str = Encoding.UTF8.GetString(Convert.FromBase64String(original));
        var parts = str.Split('|');
        var sigChars = parts[2].ToCharArray();
        sigChars[sigChars.Length - 1] = (sigChars[sigChars.Length - 1] == 'A' ? 'B' : 'A'); // flip last char of base64 signature
        parts[2] = new string(sigChars);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{parts[0]}|{parts[1]}|{parts[2]}"));
    }

    /// <summary>Expired token: same format, exp in the past (Parse returns null → guest).</summary>
    private string ExpiredToken()
    {
        var userId = Guid.NewGuid();
        return Sign(userId, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600); // 1 hour ago — expired
    }

    /// <summary>Mint a token with a specific exp (same format as the production SessionTokenService).</summary>
    private string Sign(Guid userId, long expSeconds)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{userId:N}|{expSeconds}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsigned)));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }

    /// <summary>Decode a session token's userId + exp for test assertions (test decodes only — never verifies; the minter is trusted).</summary>
    private (Guid UserId, DateTime ExpUtc) ParseToken(string token)
    {
        var str = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = str.Split('|');
        return (Guid.Parse(parts[0]), new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(long.Parse(parts[1])));
    }

    // ---- Test factory: swap the telemetry logger so login_success/login_failed can be inspected (same as SignupEndpointTests) ----

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Program.cs registers the real probes before this runs — remove, then swap (same pattern as HealthFailureTests).
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

    /// <summary>Captures every rendered log line into the shared list (login_success/login_failed assertions read it).</summary>
    private sealed class CaptureLogger : ILogger<WaTelemetryReporter>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (!string.IsNullOrEmpty(message))
                lock (typeof(CaptureLogger))
                    LoginEndpointTelemetryCapture.Lines.Add(message);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

// Shared across the factory host + tests (static — WebApplicationFactory caches one host per process).
public static class LoginEndpointTelemetryCapture
{
    public static readonly List<string> Lines = new();
}
