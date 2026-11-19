using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-001 US-021-04 phase B: PUT/GET /api/v1/auth/me/logo and the recipient logo SAS. FR-021-3..5, AC-021-1 (logo half),
/// AC-021-4, EC-021-2, EC-021-3, EC-021-5. feature.branding is seeded off, so it is flipped BEFORE the first request.
/// Each fact keeps to at most three /auth calls (5/min rate rule).
/// </summary>
public class OrgLogoUploadTests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private BlobServiceClient _blobs = null!;

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

        // Gate on before the first request: the flags cache would otherwise hold the seeded "false" for its TTL.
        var flag = await _db.Set<FeatureFlag>().SingleAsync(f => f.Key == "feature.branding");
        flag.Value = "true";
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _blobs = new BlobServiceClient(accountUrl, new Azure.Storage.StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        // The startup bootstrap races the first request, so the containers are ensured here too (idempotent).
        await _blobs.GetBlobContainerClient(BlobPaths.AvatarsContainer).CreateIfNotExistsAsync();
        await _blobs.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new WebApplicationFactory<Program>();
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
    public async Task PutLogo_ProPng_StoresBlobAndPath_RecipientGetsInlineSas() // FR-021-3, AC-021-1 logo half
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        await SeedActiveTransferAsync(ownerId, "a1b2c3d4");

        using var response = await _client.SendAsync(Put(ownerId, PngBytes(64)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("logoUrl").GetString()));
        Assert.True(body.GetProperty("brandingEnabled").GetBoolean());

        Assert.Equal(BlobPaths.OrgLogo(ownerId), await ColumnAsync(ownerId));
        var blob = LogoBlob(ownerId);
        Assert.Equal("image/png", (await blob.GetPropertiesAsync()).Value.ContentType);

        var recipient = await GetPublicAsync("a1b2c3d4");
        var url = recipient.GetProperty("orgLogoUrl").GetString()!;
        Assert.Contains($"/avatars/{ownerId:N}/logo?", url);
        Assert.Contains("sp=r", url);
        Assert.Contains("rscd=inline", url);
    }

    [Fact]
    public async Task PutLogo_OverLimit_Returns422LogoTooLarge_NoPartialState() // AC-021-4
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        var cap = await ProCapAsync();

        using var response = await _client.SendAsync(Put(ownerId, PngBytes((int)cap + 1)));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("LOGO_TOO_LARGE", problem.GetProperty("code").GetString());
        Assert.Contains(cap.ToString(), problem.GetProperty("details").GetString());
        Assert.Null(await ColumnAsync(ownerId));
        Assert.False((await LogoBlob(ownerId).ExistsAsync()).Value);
    }

    [Fact]
    public async Task PutLogo_Gif_Returns415LogoTypeInvalid() // FR-021-5
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");

        using var response = await _client.SendAsync(Put(ownerId, "GIF89a\u0001\u0001"u8.ToArray()));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("LOGO_TYPE_INVALID", problem.GetProperty("code").GetString());
        Assert.Null(await ColumnAsync(ownerId));
        Assert.False((await LogoBlob(ownerId).ExistsAsync()).Value);
    }

    [Fact]
    public async Task PutLogo_FreeOwner_Returns403Forbidden() // FR-021-4
    {
        var ownerId = await SeedOwnerAsync("free", "Studio Nova");

        using var response = await _client.SendAsync(Put(ownerId, PngBytes(64)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Null(await ColumnAsync(ownerId));
        Assert.False((await LogoBlob(ownerId).ExistsAsync()).Value);
    }

    [Fact]
    public async Task PutLogo_Guest_Returns401()
    {
        using var response = await _client.SendAsync(Put(null, PngBytes(64)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLogo_Guest_Returns401()
    {
        using var response = await _client.SendAsync(Request(HttpMethod.Get, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchMe_OrgName_RoundTripsThroughGetMe() // FR-021-1, FR-021-5
    {
        var ownerId = await SeedOwnerAsync("pro", null);

        using var patch = await _client.SendAsync(MeRequest(HttpMethod.Patch, ownerId, "{\"orgName\":\"  Studio Nova \"}"));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("Studio Nova", (await ReadJsonAsync(patch)).GetProperty("orgName").GetString());

        using var get = await _client.SendAsync(MeRequest(HttpMethod.Get, ownerId, null));
        Assert.Equal("Studio Nova", (await ReadJsonAsync(get)).GetProperty("orgName").GetString());
        Assert.Equal("Studio Nova", await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.OrgName).SingleAsync());
    }

    [Fact]
    public async Task PatchMe_OrgNameTooLong_Returns400Validation_NothingStored()
    {
        var ownerId = await SeedOwnerAsync("pro", "Keep");
        var tooLong = new string('a', AppUser.OrgNameMaxLength + 1);

        using var response = await _client.SendAsync(MeRequest(HttpMethod.Patch, ownerId, $"{{\"orgName\":\"{tooLong}\"}}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("VALIDATION", problem.GetProperty("code").GetString());
        Assert.Equal("orgName", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        Assert.Equal("Keep", await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.OrgName).SingleAsync());
    }

    [Fact]
    public async Task PatchMe_NullOrAbsentOrgName_ClearsOrKeeps()
    {
        var ownerId = await SeedOwnerAsync("pro", "Keep");

        using var rename = await _client.SendAsync(MeRequest(HttpMethod.Patch, ownerId, "{\"displayName\":\"Alice B\"}"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Keep", await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.OrgName).SingleAsync());

        using var clear = await _client.SendAsync(MeRequest(HttpMethod.Patch, ownerId, "{\"orgName\":null}"));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null(await _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == ownerId).Select(u => u.OrgName).SingleAsync());
    }

    [Fact]
    public async Task PutLogo_Svg_StoredAsSvgXml_OwnerGetHasInlineSas() // EC-021-3
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");

        using var put = await _client.SendAsync(Put(ownerId, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using var get = await _client.SendAsync(Request(HttpMethod.Get, ownerId, null));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/svg+xml", (await LogoBlob(ownerId).GetPropertiesAsync()).Value.ContentType);
        var url = (await ReadJsonAsync(get)).GetProperty("logoUrl").GetString()!;
        Assert.Contains("rscd=inline", url);
    }

    [Fact]
    public async Task PutLogo_ReplacesLegacyPath_DeletesOldBlob()
    {
        const string legacyName = "legacy-logo/logo";
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova", $"transfers/{legacyName}");
        var legacy = _blobs.GetBlobContainerClient(BlobPaths.TransfersContainer).GetBlobClient(legacyName);
        await legacy.UploadAsync(new BinaryData(new byte[8]), overwrite: true);

        using var response = await _client.SendAsync(Put(ownerId, PngBytes(64)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await legacy.ExistsAsync()).Value);
        Assert.Equal(BlobPaths.OrgLogo(ownerId), await ColumnAsync(ownerId));
    }

    [Fact]
    public async Task FreeOwnerWithLogo_RecipientOrgLogoUrlNull_ColumnKept() // EC-021-5
    {
        var ownerId = await SeedOwnerAsync("free", "Studio Nova");
        var canonical = BlobPaths.OrgLogo(ownerId);
        await _db.Set<AppUser>().Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgLogoBlobPath, canonical));
        await LogoBlob(ownerId).UploadAsync(new BinaryData(PngBytes(16)), overwrite: true);
        await SeedActiveTransferAsync(ownerId, "b1b2c3d4");

        var recipient = await GetPublicAsync("b1b2c3d4");

        Assert.Equal(JsonValueKind.Null, recipient.GetProperty("orgLogoUrl").ValueKind);
        Assert.Equal(canonical, await ColumnAsync(ownerId));
    }

    [Fact]
    public async Task ProOwnerLogoPathSet_BlobMissing_OrgLogoUrlNull() // EC-021-2 graceful half (telemetry is pinned in the unit test)
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        await _db.Set<AppUser>().Where(u => u.Id == ownerId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgLogoBlobPath, BlobPaths.OrgLogo(ownerId)));
        await SeedActiveTransferAsync(ownerId, "c1b2c3d4");

        var recipient = await GetPublicAsync("c1b2c3d4");

        Assert.Equal("Studio Nova", recipient.GetProperty("orgName").GetString());
        Assert.Equal(JsonValueKind.Null, recipient.GetProperty("orgLogoUrl").ValueKind);
    }

    [Fact]
    public async Task GetLogo_Pro_ReturnsCapAndBrandingEnabled()
    {
        var ownerId = await SeedOwnerAsync("pro", "Studio Nova");
        var cap = await ProCapAsync();

        using var response = await _client.SendAsync(Request(HttpMethod.Get, ownerId, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("logoUrl").ValueKind);
        Assert.Equal(cap, body.GetProperty("maxLogoSize").GetInt64());
        Assert.True(body.GetProperty("brandingEnabled").GetBoolean());
    }

    private async Task<long> ProCapAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ILimitsProvider>().Resolve("pro", CancellationToken.None)).MaxLogoSize;
    }

    private BlobClient LogoBlob(Guid userId) =>
        _blobs.GetBlobContainerClient(BlobPaths.AvatarsContainer).GetBlobClient($"{userId:N}/logo");

    private Task<string?> ColumnAsync(Guid userId) =>
        _db.Set<AppUser>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.OrgLogoBlobPath).SingleAsync();

    private static byte[] PngBytes(int total)
    {
        var bytes = new byte[total];
        PngHead.CopyTo(bytes, 0);
        return bytes;
    }

    private static HttpRequestMessage Put(Guid? userId, byte[] body)
    {
        var request = Request(HttpMethod.Put, userId, body);
        request.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return request;
    }

    private static HttpRequestMessage MeRequest(HttpMethod method, Guid? userId, string? json)
    {
        var request = new HttpRequestMessage(method, "/api/v1/auth/me");
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return Authorize(request, userId);
    }

    private static HttpRequestMessage Request(HttpMethod method, Guid? userId, byte[]? body)
    {
        var request = new HttpRequestMessage(method, "/api/v1/auth/me/logo");
        if (body is not null)
            request.Content = new ByteArrayContent(body);
        return Authorize(request, userId);
    }

    private static HttpRequestMessage Authorize(HttpRequestMessage request, Guid? userId)
    {
        if (userId is { } id)
        {
            var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
            var unsigned = $"{id:N}|{exp}";
            var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
            request.Headers.Add("Cookie", $"wa.session={Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"))}");
        }
        return request;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetPublicAsync(string linkId)
    {
        using var response = await _client.GetAsync($"/api/v1/public/transfers/{linkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<Guid> SeedOwnerAsync(string planCode, string? orgName, string? orgLogoBlobPath = null)
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"logo-{Guid.NewGuid():N}@example.com",
            DisplayName = "Alice",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow,
            OrgName = orgName,
            OrgLogoBlobPath = orgLogoBlobPath
        };
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task SeedActiveTransferAsync(Guid ownerId, string linkId)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = linkId,
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 5,
            DownloadsCount = 0,
            SenderName = "Alice",
            OwnerAppUserId = ownerId,
            TotalBytes = 0,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
