using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-003 US-023-01: GET /api/v1/transfers/{id}/analytics (FR-023-1 aggregates, FR-023-5 gate, AC-023-1/2/3, EC-023-1..6).
/// feature.analytics is seeded off, so the gate-on class flips it BEFORE the first request (flags cache TTL); the gate-off class
/// leaves the seed alone. Same container recipe as ScheduledSendEndpointTests.
/// </summary>
public abstract class TransferAnalyticsEndpointTestsBase(bool gateOn) : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";

    private AzuriteContainer _azurite = null!;
    protected MsSqlContainer Sql = null!;
    private WebApplicationFactory<Program> _factory = null!;
    protected WaDbContext Db = null!;
    protected HttpClient Client = null!;

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
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        Sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await Sql.StartAsync();
        var connString = Sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        Db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await Db.Database.MigrateAsync();

        if (gateOn)
        {
            var flag = await Db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Analytics);
            flag.Value = "true";
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        var svc = new BlobServiceClient(accountUrl, new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await Db.DisposeAsync();
        await Sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Helpers ----

    protected async Task<Guid> SeedUserAsync(string planCode)
    {
        var planId = await Db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"analytics-{Guid.NewGuid():N}@example.com",
            DisplayName = "Analytics Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Set<AppUser>().Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    protected async Task<Guid> SeedTransferAsync(Guid? ownerId, byte status = TransferStatus.Active, DateTime? createdUtc = null, bool deleted = false)
    {
        var id = Guid.NewGuid();
        var created = createdUtc ?? DateTime.UtcNow.AddDays(-10);
        Db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = Guid.NewGuid().ToString("N")[..8],
            OwnerAppUserId = ownerId,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 1000,
            SenderName = "Anna K.",
            TotalBytes = 1,
            FileCount = 1,
            CreatedAtUtc = created,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        });
        await Db.SaveChangesAsync();
        return id;
    }

    protected static DownloadEvent Event(Guid transferId, DateTime atUtc, string? country, string? family, Guid? fileId = null) => new()
    {
        FileId = fileId,
        TransferId = transferId,
        IpHash = "IPHASH-SENTINEL",
        UaHash = "UAHASH-SENTINEL",
        Country = country,
        UserAgentFamily = family,
        CreatedAtUtc = atUtc
    };

    /// <summary>US-023-02: FileItem (+ BlobRef, UQ_FileItem_T_B) per name, SortOrder = index; returns the FileItem ids.</summary>
    protected async Task<Guid[]> SeedFilesAsync(Guid transferId, params string[] names)
    {
        var ids = new Guid[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            var blobRef = new BlobRef
            {
                Id = Guid.NewGuid(),
                BlobPath = BlobPaths.TransferFile(transferId, ids[i]),
                SizeBytes = 1,
                RefCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            };
            Db.Set<BlobRef>().Add(blobRef);
            Db.Set<FileItem>().Add(new FileItem
            {
                Id = ids[i],
                TransferId = transferId,
                BlobRefId = blobRef.Id,
                OriginalName = names[i],
                SizeBytes = 1,
                ContentType = "text/plain",
                SortOrder = i
            });
        }

        var transfer = await Db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = names.Length;
        await Db.SaveChangesAsync();
        return ids;
    }

    protected async Task AddEventsAsync(params DownloadEvent[] events)
    {
        Db.Set<DownloadEvent>().AddRange(events);
        await Db.SaveChangesAsync();
    }

    /// <summary>TA-9.1 session cookie (same idiom as PlanUsageEndpointTests).</summary>
    private static string CookieFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        return "wa.session=" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }

    protected async Task<HttpResponseMessage> GetAsync(string url, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (userId is { } id) request.Headers.Add("Cookie", CookieFor(id));
        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> AnalyticsAsync(Guid transferId, Guid? userId) =>
        GetAsync($"/api/v1/transfers/{transferId}/analytics", userId);

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }
}
