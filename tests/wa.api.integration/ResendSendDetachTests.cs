using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
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
/// T-003-10 (AC-010-1 send-time flip): POST /resend then POST /send on the re-send draft. SendStore.SaveSendAsync flips
/// SupersededBy on the ORIGINAL via raw SQL; the original is tracked in the singleton context (the resend handler loaded it), so the
/// tracker detach must be a real detach — Local.Remove marked it Deleted and the same method's SaveChanges tried to delete the row (500).
/// Fresh Azurite + SQL 2022 per instance (TA-14.3).
/// </summary>
public class ResendSendDetachTests : IAsyncLifetime
{
    private const string CookieName = "wa.session";
    private const string JwtSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private string _connString = null!;
    private BlobServiceClient _svc = null!;
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
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", JwtSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");
        // TA-4.1.7 / EC-054-1: the outbox row commits via SbEventPublisher's own context BEFORE broker publish; a dummy connection string is deterministic.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", _connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        await _db.Database.MigrateAsync();

        _svc = new BlobServiceClient(
            accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new TestFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // Cookie headers are set explicitly per request
        });
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
    public async Task ResendThenSend_201_OriginalKeptWithSupersededBy_ContextStaysHealthy()
    {
        var ownerId = await SeedOwnerAsync();
        var origId = await SeedActiveOriginalWithBlobAsync(ownerId);
        var cookie = CookieValueFor(ownerId);

        // Resend: the app's singleton context loads (and tracks) the original.
        using var resend = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/{origId:N}/resend");
        resend.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={cookie}");
        using var resendResponse = await _client.SendAsync(resend);
        Assert.Equal(HttpStatusCode.Created, resendResponse.StatusCode);

        string draftIdText, linkId;
        using (var doc = JsonDocument.Parse(await resendResponse.Content.ReadAsByteArrayAsync()))
        {
            draftIdText = doc.RootElement.GetProperty("id").GetString()!;
            linkId = doc.RootElement.GetProperty("linkId").GetString()!;
        }
        var draftId = new Guid(draftIdText);

        // Send the draft: SaveSendAsync flips SupersededBy on the tracked original (today: "association severed" 500).
        using var send = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/{draftId:N}/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { emails = Array.Empty<string>(), senderName = "Alice" }), Encoding.UTF8, "application/json")
        };
        send.Headers.Add("Idempotency-Key", $"key-resend-send-{Guid.NewGuid():N}");
        send.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={cookie}");
        using var sendResponse = await _client.SendAsync(send);
        Assert.Equal(HttpStatusCode.Created, sendResponse.StatusCode);

        // Singleton context holds no Deleted entry (read between awaited calls only).
        var appDb = _factory.Services.GetRequiredService<WaDbContext>();
        Assert.DoesNotContain(appDb.ChangeTracker.Entries(), e => e.State == EntityState.Deleted);

        // The original row survives, flipped to SupersededBy = the new transfer, still Active.
        using var freshDb = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var original = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == origId);
        Assert.Equal(draftId, original.SupersededBy);
        Assert.Equal(1, original.Status);

        // Later request on the shared context stays healthy.
        using var publicGet = await _client.GetAsync($"/api/v1/public/transfers/{linkId}");
        Assert.Equal(HttpStatusCode.OK, publicGet.StatusCode);
    }

    private async Task<Guid> SeedOwnerAsync()
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == ILimitsProvider.GuestPlanCode)
            .Select(p => p.Id).FirstAsync();
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"t0310-{Guid.NewGuid():N}@example.com",
            DisplayName = "T-003-10 Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(owner);
        await _db.SaveChangesAsync();
        return owner.Id;
    }

    /// <summary>Active owned original + BlobRef + FileItem, seeded via the test context, with the blob physically present (the FILES_GONE gate checks it).</summary>
    private async Task<Guid> SeedActiveOriginalWithBlobAsync(Guid ownerId)
    {
        var now = DateTime.UtcNow;
        var origId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var blobRef = new BlobRef
        {
            Id = Guid.NewGuid(),
            BlobPath = BlobPaths.TransferFile(origId, fileId),
            SizeBytes = 16L,
            RefCount = 1,
            CreatedAtUtc = now
        };
        _db.Set<BlobRef>().Add(blobRef);
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = origId,
            LinkId = wa.domain.ValueObjects.LinkId.Mint().Value,
            Status = 1,
            OwnerAppUserId = ownerId,
            TotalBytes = 16L,
            FileCount = 1,
            SenderName = "Alice",
            MaxDownloads = 5,
            ExpiresAtUtc = now.AddDays(30),
            CreatedAtUtc = now
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = fileId,
            TransferId = origId,
            BlobRefId = blobRef.Id,
            OriginalName = "orig.txt",
            SizeBytes = 16L,
            ContentType = "text/plain",
            SortOrder = 0
        });
        await _db.SaveChangesAsync();

        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        var path = blobRef.BlobPath;
        var cut = path.IndexOf('/');
        using var ms = new MemoryStream(bytes);
        await _svc.GetBlobContainerClient(path[..cut]).GetBlobClient(path[(cut + 1)..]).UploadAsync(ms, true, default);
        return origId;
    }

    /// <summary>TA-9.1 provisional scheme: Base64(userId|exp|sig), sig = HMAC-SHA256(userId|exp, Wa:Jwt:Secret).</summary>
    private static string CookieValueFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(JwtSecret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId:N}|{exp}")));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId:N}|{exp}|{sig}"));
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
