using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-046-01 (AC-046-1, FR-046-2/5/6, EC-046-1/2): PATCH/GET /api/v1/auth/me emailNotifications over a real host.
/// Fresh Azurite + SQL 2022 per fact (cold FlagsCache); each fact stays within the 5/min auth rate rule (≤ 4 requests).
/// </summary>
public class EmailNotificationsSettingTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}").ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        var planId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _userId = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = _userId,
            Email = "notify-user@x.com",
            DisplayName = "Notify User",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    /// <summary>Starts the host AFTER the flag row is set (the FlagsCache is cold per fact).</summary>
    private async Task StartAsync(bool flagOn)
    {
        var flag = await _db.Set<FeatureFlag>().FirstAsync(f => f.Key == FeatureGates.EmailSettings);
        flag.Value = flagOn ? "true" : "false";
        await _db.SaveChangesAsync();
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private HttpRequestMessage Req(HttpMethod method, string? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/v1/auth/me");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        // Forged wa.session cookie — same idiom as DataExportEndpointTests.Req.
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{_userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        request.Headers.Add("Cookie", $"wa.session={Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"))}");
        return request;
    }

    private async Task<bool?> ReadColumnAsync() => await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == _userId).Select(u => u.EmailNotificationsEnabled).FirstAsync();

    private static async Task<JsonElement> JsonOf(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task NewUser_ColumnIsNullableBitAndNull_GetReturnsTrue() // EC-046-2: never touched = on; the column is BIT NULL
    {
        var dataType = Assert.Single(await _db.Database.SqlQueryRaw<string>("SELECT DATA_TYPE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AppUser' AND COLUMN_NAME = 'EmailNotificationsEnabled'").ToListAsync());
        var isNullable = Assert.Single(await _db.Database.SqlQueryRaw<string>("SELECT IS_NULLABLE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AppUser' AND COLUMN_NAME = 'EmailNotificationsEnabled'").ToListAsync());
        Assert.Equal(("bit", "YES"), (dataType, isNullable));

        await StartAsync(flagOn: true);
        using var response = await _client!.SendAsync(Req(HttpMethod.Get));
        Assert.Null(await ReadColumnAsync()); // seeded row reads null — never touched
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await JsonOf(response)).GetProperty("emailNotifications").GetBoolean()); // null column → effective true
    }

    [Fact]
    public async Task Patch_FlagOn_False_PersistsFalse_GetReturnsFalse() // AC-046-1: the switch off persists and echoes back; true again re-enables
    {
        await StartAsync(flagOn: true);

        using var patchOff = await _client!.SendAsync(Req(HttpMethod.Patch, "{\"emailNotifications\":false}"));
        Assert.Equal(HttpStatusCode.OK, patchOff.StatusCode);
        Assert.False((await JsonOf(patchOff)).GetProperty("emailNotifications").GetBoolean());
        Assert.False(await ReadColumnAsync());

        using var get = await _client.SendAsync(Req(HttpMethod.Get));
        Assert.False((await JsonOf(get)).GetProperty("emailNotifications").GetBoolean());

        using var patchOn = await _client.SendAsync(Req(HttpMethod.Patch, "{\"emailNotifications\":true}"));
        Assert.Equal(HttpStatusCode.OK, patchOn.StatusCode);
        Assert.True(await ReadColumnAsync()); // scenario 2: turned back on → sent again (gate reads at send time)
    }

    [Fact]
    public async Task Patch_ThemeOnly_LeavesSettingUnchanged() // FR-046-2 additive / EC-046-5: absent leaves the column alone
    {
        await StartAsync(flagOn: true);
        _db.Set<AppUser>().Where(u => u.Id == _userId).ExecuteUpdate(s => s.SetProperty(u => u.EmailNotificationsEnabled, false)); // tests may write the column directly (EC-046-4)

        using var response = await _client!.SendAsync(Req(HttpMethod.Patch, "{\"theme\":\"dark\"}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonOf(response)).GetProperty("emailNotifications").GetBoolean()); // still false — untouched by the theme PATCH
        Assert.False(await ReadColumnAsync());
    }

    [Fact]
    public async Task Patch_FlagOff_Returns404FeatureDisabled_NoWrite() // FR-046-6: only editable while feature.emailSettings is on
    {
        await StartAsync(flagOn: false);

        using var patch = await _client!.SendAsync(Req(HttpMethod.Patch, "{\"emailNotifications\":false}"));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal("FEATURE_DISABLED", (await JsonOf(patch)).GetProperty("code").GetString());
        Assert.Null(await ReadColumnAsync()); // no write happened

        using var themeOnly = await _client.SendAsync(Req(HttpMethod.Patch, "{\"theme\":\"dark\"}"));
        Assert.Equal(HttpStatusCode.OK, themeOnly.StatusCode); // the other fields stay editable while off
    }

    [Fact]
    public async Task Gate_RealStore_ReadsAtSendTime() // AC-046-2 / EC-046-1: no cache — the column is read on every MaySendAsync call (no host)
    {
        var options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_db.Database.GetConnectionString()).Options;
        IFlagResolver flagsOn = new AlwaysTrueFlags();
        var gate = new OwnerNotificationGate(new EmailNotificationsStore(new AppDbContextFactory(options)), flagsOn, NullLogger<OwnerNotificationGate>.Instance);

        _db.Set<AppUser>().Where(u => u.Id == _userId).ExecuteUpdate(s => s.SetProperty(u => u.EmailNotificationsEnabled, false));
        Assert.False(await gate.MaySendAsync(_userId, CancellationToken.None)); // row false → suppressed at send time

        _db.Set<AppUser>().Where(u => u.Id == _userId).ExecuteUpdate(s => s.SetProperty(u => u.EmailNotificationsEnabled, true));
        Assert.True(await gate.MaySendAsync(_userId, CancellationToken.None)); // updated to true on the next call — no cache (EC-046-1)

        Assert.True(await gate.MaySendAsync(Guid.NewGuid(), CancellationToken.None)); // unknown Guid → null column → on
    }

    private sealed class AlwaysTrueFlags : IFlagResolver // inline flag stub returning true (the gate's column read is the point of this fact)
    {
        public Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default) => Task.FromResult((T)(object)true);
    }
}
