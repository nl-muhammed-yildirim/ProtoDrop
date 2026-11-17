using System.Net;
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
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-003-04 exit checks (TA-4.2#5, TA-9.1/9.4/9.6): POST /api/v1/public/transfers/{linkId}/unlock + endpoint 4's ?t= acceptance.
/// Correct password → { token } with the exact HS256 claim set; wrong password → WRONG_PASSWORD Problem+JSON (401);
/// terminal rows ignore credentials and tokens (D6); telemetry never carries the password value (TA-9.4).
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as PublicTransferEndpointTests.
/// </summary>
public class UnlockEndpointTests : IAsyncLifetime
{
    // Program.cs captures env at host build — set all before the per-instance factory is created (same pattern as PublicTransferEndpointTests).
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

        UnlockEndpointTelemetryCapture.Lines.Clear(); // per-test isolation: assertions below cover only this test's requests (fresh host per instance)

        _factory = new TestFactory();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Exit check 1: correct password → 200 + JWT { sub, pwd:true, exp≈now+7d, jti }; re-query endpoint 4 with ?t= → no re-prompt (file list still returned) ----

    [Fact]
    public async Task CorrectPassword_ReturnsToken_WithExactClaimSet_AndEndpoint4AcceptsIt()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "a2b3c4d5", password: "correct horse");
        await SeedFileItemsAsync(transferId, [(Guid.NewGuid(), "first.txt", 1000L)]);

        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/a2b3c4d5/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "correct horse" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // TA-4.2#5 exact wire shape: exactly one property — token. No Set-Cookie, no extras (T-003-05: sessionStorage is client-side).
        Assert.Single(doc.RootElement.EnumerateObject().Select(p => p.Name), "token");
        var token = doc.RootElement.GetProperty("token").GetString()!;

        // Decode the HS256 JWT (test decodes only — Program.cs's minter is the trusted source) and pin the TA-4.2#5 claim set.
        var claims = DecodeJwtPayload(token, out var alg);
        Assert.Equal("HS256", alg); // header declares HS256 (TA-4.2#5)
        Assert.Equal("a2b3c4d5", claims.GetProperty("sub").GetString()); // sub IS the linkId (the only bearer identifier in this endpoint family)
        Assert.True(claims.GetProperty("pwd").GetBoolean()); // pwd is always true on a minted token (TA-9.1: the unlock marker)
        var expSeconds = claims.GetProperty("exp").GetInt64();
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(expSeconds, nowSeconds + 7 * 86400 - 30, nowSeconds + 7 * 86400 + 30); // exactly now+7d (TA-9.1) within clock skew
        Assert.False(string.IsNullOrEmpty(claims.GetProperty("jti").GetString())); // jti present — replay-of-extracted-token is auditable

        // Endpoint 4 with ?t= → passwordRequired flips to false; the full file list is still returned (orchestrator ruling: gated pages keep the payload).
        using var getResponse = await _client.GetAsync("/api/v1/public/transfers/a2b3c4d5?t=" + token);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsByteArrayAsync());
        Assert.False(getDoc.RootElement.GetProperty("passwordRequired").GetBoolean()); // no re-prompt (AC-003-2 server half)

        var filesEl = getDoc.RootElement.GetProperty("files");
        Assert.Equal(1, filesEl.GetArrayLength());
        Assert.Equal("first.txt", filesEl[0].GetProperty("name").GetString()); // file list still visible — the M0 gate is client-side
    }

    [Fact]
    public async Task MintedToken_ExpIsExactlyNowPlusSevenDays()
    {
        await SeedTransferAsync(status: 1, linkId: "a6b7c8d9", password: "password-123");

        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/a6b7c8d9/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "password-123" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var token = doc.RootElement.GetProperty("token").GetString()!;

        var claims2 = DecodeJwtPayload(token, out _);
        var expSeconds = claims2.GetProperty("exp").GetInt64();
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // TA-9.1: exp is exactly now + 7 days — this integration test bounds the window at ±2s; the EXACT pin (Assert.Equal with a pinned
        // injectable clock) lives in UnlockTokenTests.Mint_ExactExpClaim_IsNowPlusSevenDaysViaPinnedClock (D3).
        Assert.InRange(expSeconds, nowSeconds + (7 * 86400 - 2), nowSeconds + (7 * 86400 + 2));
    }

    // ---- Exit check 2: wrong password → WRONG_PASSWORD Problem+JSON (401); file list still not visible via ?t= path ----

    [Fact]
    public async Task WrongPassword_ReturnsWrongPassword_ProblemJson_NoToken()
    {
        await SeedTransferAsync(status: 1, linkId: "b2c3d4e5", password: "right-password");

        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/b2c3d4e5/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "wrong-password" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // 401 — the closed table's status for WRONG_PASSWORD

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal(ProblemCodes.WrongPassword, root.GetProperty("code").GetString()); // exact TA-4.1.3 code — no new closed-list member (TA-4.1.3)
        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal("Wrong password", root.GetProperty("title").GetString()); // title pinned by the closed table
        Assert.True(root.TryGetProperty("type", out _));
        Assert.False(doc.RootElement.TryGetProperty("token", out _)); // no token minted on wrong password

        // Endpoint 4 still gated — no valid token was issued.
        using var getResponse = await _client.GetAsync("/api/v1/public/transfers/b2c3d4e5");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsByteArrayAsync());
        Assert.True(getDoc.RootElement.GetProperty("passwordRequired").GetBoolean()); // still gated (flag stays true)
    }

    // ---- Missing body/field → VALIDATION Problem+JSON with errors[] for field "password" (D1) — no DB access ----

    [Fact]
    public async Task EmptyBody_ReturnsValidation_ProblemJson_WithPasswordFieldError()
    {
        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/a4b5c6d7/unlock",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // 400 — the closed table's status for VALIDATION

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal(ProblemCodes.Validation, root.GetProperty("code").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Validation failed", root.GetProperty("title").GetString());

        // TA-4.1.3 errors[] — the only code with per-field details; field name pinned to "password".
        var errorsEl = root.GetProperty("errors");
        Assert.Equal(1, errorsEl.GetArrayLength());
        Assert.Equal("password", errorsEl[0].GetProperty("field").GetString());
        Assert.False(string.IsNullOrEmpty(errorsEl[0].GetProperty("message").GetString()));
    }

    [Fact]
    public async Task NullPasswordValue_ReturnsValidation_ProblemJson_WithPasswordFieldError()
    {
        // JSON null → the password variable stays null → VALIDATION (D1) — same shape as the empty-body case.
        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/e5f6g7h8/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = (string?)null }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.Validation, doc.RootElement.GetProperty("code").GetString());
        var errorsEl = doc.RootElement.GetProperty("errors");
        Assert.Equal(1, errorsEl.GetArrayLength());
        Assert.Equal("password", errorsEl[0].GetProperty("field").GetString()); // exact field name from D1's ProblemFieldError
    }

    // ---- Exit check 3 (US-003-05): status wins over credentials — terminal rows show their terminal screen regardless of tokens ----

    [Fact]
    public async Task ExpiredTransfer_IgnoresCredentials_ReturnsExpiredScreen()
    {
        await SeedTransferAsync(status: 2, linkId: "c5d6e7f8"); // Status=2 Expired — no password hash stored at all

        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/c5d6e7f8/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "anything" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode); // 410 — the terminal-state mapping wins (D6: status BEFORE any password logic)

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.TransferExpired, doc.RootElement.GetProperty("code").GetString());

        // Endpoint 4 with any token still shows expired (US-003-05: "earlier of the two wins" — expiry beats unlock).
        using var getResponse = await _client.GetAsync("/api/v1/public/transfers/c5d6e7f8?t=some-token");
        Assert.Equal(HttpStatusCode.Gone, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeletedTransfer_IgnoresCredentials_ReturnsNotFoundScreen()
    {
        // FR-003-9 / DECISION A: Status=4 (Deleted) maps to the same body/status as unknown linkIds — deliberately indistinguishable.
        await SeedTransferAsync(status: 4, linkId: "d6e7f8a9");

        using var response = await _client.PostAsync(
            "/api/v1/public/transfers/d6e7f8a9/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "secret" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404 — the unknown-case status

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.TransferExpired, doc.RootElement.GetProperty("code").GetString()); // same code as expired/unknown (DECISION A)
    }

    [Fact]
    public async Task UnknownLinkId_Returns404_BodyByteIdenticalToEndpoint4UnknownCase()
    {
        // M1 fix: plan §5 row 7 — endpoint 5's unknown-linkId case must be byte-identical to endpoint 4's (FR-003-9 single-body invariant).
        const string traceparent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbccccddeeff01-01"; // D8: same W3C trace id → matching correlationId

        using var unknownReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/public/transfers/zzz99999/unlock");
        unknownReq.Headers.Add("traceparent", traceparent);
        unknownReq.Content = new StringContent(JsonSerializer.Serialize(new { password = "whatever" }), Encoding.UTF8, "application/json");
        using var unknownRes = await _client.SendAsync(unknownReq);
        Assert.Equal(HttpStatusCode.NotFound, unknownRes.StatusCode); // 404 — the unknown-case status

        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/zzz99999");
        getReq.Headers.Add("traceparent", traceparent);
        using var getRes = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        // Both bodies parse as Problem+JSON with code TRANSFER_EXPIRED.
        using (var unlockDoc = JsonDocument.Parse(await unknownRes.Content.ReadAsByteArrayAsync()))
            Assert.Equal(ProblemCodes.TransferExpired, unlockDoc.RootElement.GetProperty("code").GetString());
        using (var getDoc = JsonDocument.Parse(await getRes.Content.ReadAsByteArrayAsync()))
            Assert.Equal(ProblemCodes.TransferExpired, getDoc.RootElement.GetProperty("code").GetString());

        // Byte-identical bodies: the unlock path and endpoint 4's unknown case must produce exactly the same bytes (FR-003-9).
        var unlockBody = await unknownRes.Content.ReadAsByteArrayAsync();
        var getBody = await getRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(getBody, unlockBody); // FR-003-9: single body for the unknown case across both endpoints
    }

    // ---- Exit check 4: both telemetry events fire without ever carrying the password value (TA-9.4) ----

    [Fact]
    public async Task PasswordEvents_FireWithoutCarryingThePasswordValue()
    {
        await SeedTransferAsync(status: 1, linkId: "e7f8a9b0", password: "super-secret-password");

        using (var wrongResponse = await _client.PostAsync(
            "/api/v1/public/transfers/e7f8a9b0/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "wrong" }), Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode); // password_wrong path
        }

        using (var okResponse = await _client.PostAsync(
            "/api/v1/public/transfers/e7f8a9b0/unlock",
            new StringContent(JsonSerializer.Serialize(new { password = "super-secret-password" }), Encoding.UTF8, "application/json")))
        {
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode); // password_correct path
        }

        // TA-9.4: no log line may contain the raw password value (only closed-name templates travel — TA-10.2).
        Assert.DoesNotContain(UnlockEndpointTelemetryCapture.Lines, line => line.Contains("super-secret-password", StringComparison.OrdinalIgnoreCase));

        // Both closed-set event names fired.
        Assert.Contains(UnlockEndpointTelemetryCapture.Lines, l => l.Contains(TelemetryEvents.PasswordWrong, StringComparison.Ordinal));
        Assert.Contains(UnlockEndpointTelemetryCapture.Lines, l => l.Contains(TelemetryEvents.PasswordCorrect, StringComparison.Ordinal));
    }

    // ---- TA-9.6: invalid / wrong-sub tokens behave exactly like an absent token — no 4xx, no format leak ----

    [Fact]
    public async Task InvalidToken_BehavesLikeAbsent_NoLeak()
    {
        await SeedTransferAsync(status: 1, linkId: "f8a9b0c1", password: "token-test-pass");

        using var response = await _client.GetAsync("/api/v1/public/transfers/f8a9b0c1?t=garbage.token.here");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // NOT a 4xx — junk behaves like absent (TA-9.6 enumeration safety)

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.True(doc.RootElement.GetProperty("passwordRequired").GetBoolean()); // flag stays true (no unlock applied)
    }

    [Fact]
    public async Task TokenForDifferentLinkId_DoesNotUnlockThisTransfer()
    {
        await SeedTransferAsync(status: 1, linkId: "a9b0c1d2", password: "right-pass");

        // Mint a token for a DIFFERENT linkId via the same DI singleton Program.cs registers (resolve from this instance's provider).
        var minter = _factory.Services.GetRequiredService<IUnlockTokenMinter>();
        var wrongSubToken = minter.Mint("b1c2d3e4"); // deliberately different sub

        using var response = await _client.GetAsync("/api/v1/public/transfers/a9b0c1d2?t=" + wrongSubToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.True(doc.RootElement.GetProperty("passwordRequired").GetBoolean()); // sub mismatch → behaves like absent (TA-9.6)
    }

    // ---- Helpers (seeded directly via WaDbContext — same pattern as PublicTransferEndpointTests; D7: rows control columns precisely) ----

    private async Task<Guid> SeedTransferAsync(int status, string linkId, string? senderEmail = null, int maxDownloads = 1, int downloadsCount = 0, long totalBytes = 0, string? password = null)
    {
        var id = Guid.NewGuid();

        // TA-9.1: hash with the SAME PasswordHasher<string> + user-id keying as SendTransfer.cs (transfer.Id.ToString("n")) —
        // any drift here means every correct password fails; these tests pin that inverse.
        string? passwordHash = null;
        if (password is not null)
            passwordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword(id.ToString("n"), password);

        _db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = linkId, // CHAR(8) — all in the LinkId charset (TA-9.6)
            Status = (byte)status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = maxDownloads,
            DownloadsCount = downloadsCount,
            SenderName = "Alice",
            SenderEmail = senderEmail,
            Note = null,
            PasswordHash = passwordHash, // the PBKDF2-SHA256 hash stored at seed time (mirrors send endpoint T-002-06)
            TotalBytes = totalBytes,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedFileItemsAsync(Guid transferId, (Guid fileId, string name, long sizeBytes)[] files)
    {
        // UQ_FileItem_T_B: (TransferId, BlobRefId) is unique — one BlobRef per FileItem (same style as FinalizeTransfer).
        for (var i = 0; i < files.Length; i++)
        {
            var (fileId, name, sizeBytes) = files[i];
            var blobRef = new BlobRef
            {
                Id = Guid.NewGuid(),
                BlobPath = $"transfers/{transferId:N}/{fileId:N}",
                SizeBytes = sizeBytes,
                RefCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Set<BlobRef>().Add(blobRef);
            _db.Set<FileItem>().Add(new FileItem
            {
                Id = fileId,
                TransferId = transferId,
                BlobRefId = blobRef.Id,
                OriginalName = name,
                SizeBytes = sizeBytes,
                ContentType = "text/plain",
                SortOrder = i // TA-4.2#4: GET returns files in SortOrder ascending
            });
        }
        // Keep Transfer columns consistent with the FileItem rows (same as finalize does in production):
        // hasDownloadAll reads Transfer.FileCount/TotalBytes, not the FileItem rows.
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = files.Length;
        transfer.TotalBytes = files.Sum(f => f.sizeBytes);
        await _db.SaveChangesAsync();
    }

    /// <summary>Decode an HS256 JWT's payload claims for test assertions (test decodes only — never verifies the signature; Program.cs's minter is trusted).</summary>
    private static JsonElement DecodeJwtPayload(string token, out string alg)
    {
        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);

        var headerJson = Encoding.UTF8.GetString(Base64UrlDecode(segments[0]));
        using (var headerDoc = JsonDocument.Parse(headerJson))
            alg = headerDoc.RootElement.GetProperty("alg").GetString()!;

        using var payloadDoc = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(segments[1])));
        return payloadDoc.RootElement.Clone();
    }

    /// <summary>Base64Url decode (RFC 7515 §2.1) — JWT's URL-safe alphabet, no padding.</summary>
    private static byte[] Base64UrlDecode(string encoded) =>
        Convert.FromBase64String(encoded.Replace('-', '+').Replace('_', '/').PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '='));

    // ---- Test factory: swap the telemetry logger so TA-9.4 can inspect every rendered log line ----

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

    /// <summary>Captures every rendered log line into the shared list (TA-9.4 password-leakage assertions read it).</summary>
    private sealed class CaptureLogger : ILogger<WaTelemetryReporter>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (!string.IsNullOrEmpty(message))
                lock (typeof(CaptureLogger))
                    UnlockEndpointTelemetryCapture.Lines.Add(message);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

// Shared across the factory host + tests (static — WebApplicationFactory caches one host per process).
public static class UnlockEndpointTelemetryCapture
{
    public static readonly List<string> Lines = new();
}
