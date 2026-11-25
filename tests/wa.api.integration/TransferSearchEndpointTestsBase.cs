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
/// US-045-01 (F-XCT-002, T-066): GET /api/v1/transfers?q= — owner-scoped search by file name or recipient behind the feature.search gate,
/// plus the widened admin q on endpoint 21. AC-045-1/2/3, EC-045-1/3/6, FR-045-5/7. Same container recipe as TransferAnalyticsEndpointTests;
/// Wa__AdminEmails + seeded admin user per AdminFlagsEndpointTests (process-global env vars — the runner is single-threaded).
/// </summary>
public abstract class TransferSearchEndpointTestsBase(bool gateOn) : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string AdminEmail = "search-admin@x.com";

    private AzuriteContainer _azurite = null!;
    protected MsSqlContainer Sql = null!;
    private WebApplicationFactory<Program> _factory = null!;
    protected WaDbContext Db = null!;
    protected HttpClient Client = null!;
    protected Guid AdminUserId = default;   // endpoint 21 (T-011-01) — seeded before the factory builds.

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
        // Role derives per request from Wa:AdminEmails (T-011-01 gate) — must be set before the host builds.
        Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail);
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=«redacted-secret»");

        Sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await Sql.StartAsync();
        var connString = Sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        Db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await Db.Database.MigrateAsync();

        if (gateOn)
        {
            var flag = await Db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Search);
            flag.Value = "true";
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        // Admin user for endpoint 21 (AdminFlagsEndpointTests recipe) — seeded before the factory builds so RequireAdminAsync can resolve it.
        var planId = await Db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();
        AdminUserId = Guid.NewGuid();
        Db.Set<AppUser>().Add(new AppUser
        {
            Id = AdminUserId,
            Email = AdminEmail.ToLowerInvariant(),
            DisplayName = "Search Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

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

    // ---- Helpers (TransferAnalyticsEndpointTests recipe; SeedTransferAsync gains senderName) ----

    protected async Task<Guid> SeedUserAsync(string planCode = "pro")
    {
        var planId = await Db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"search-{Guid.NewGuid():N}@example.com",
            DisplayName = "Search Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Set<AppUser>().Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    protected async Task<Guid> SeedTransferAsync(Guid? ownerId, byte status = TransferStatus.Active, DateTime? createdUtc = null, bool deleted = false, string senderName = "Anna K.")
    {
        var id = Guid.NewGuid();
        Db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = Guid.NewGuid().ToString("N")[..8],
            OwnerAppUserId = ownerId,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 1000,
            SenderName = senderName,
            TotalBytes = 1,
            FileCount = 1,
            CreatedAtUtc = createdUtc ?? DateTime.UtcNow.AddDays(-10),
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        });
        await Db.SaveChangesAsync();
        return id;
    }

    /// <summary>US-045-01: FileItem (+ BlobRef) per name, SortOrder = index — the search matches OriginalName.</summary>
    protected async Task SeedFilesAsync(Guid transferId, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var fileId = Guid.NewGuid();
            var blobRefId = Guid.NewGuid();   // explicit id — mirrors TransferAnalyticsEndpointTests.SeedFilesAsync.
            Db.Set<BlobRef>().Add(new BlobRef
            {
                Id = blobRefId,
                BlobPath = BlobPaths.TransferFile(transferId, fileId),
                SizeBytes = 1,
                RefCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            });
            Db.Set<FileItem>().Add(new FileItem
            {
                Id = fileId,
                TransferId = transferId,
                BlobRefId = blobRefId,
                OriginalName = names[i],
                SizeBytes = 1,
                ContentType = "text/plain",
                SortOrder = i
            });
        }

        var transfer = await Db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = names.Length;   // mirrors TransferAnalyticsEndpointTests.SeedFilesAsync.
        await Db.SaveChangesAsync();
    }

    protected async Task SeedRecipientAsync(Guid transferId, string address)
    {
        Db.Set<EmailRecipient>().Add(new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            Address = address
        });
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
        if (userId is not null) request.Headers.Add("Cookie", CookieFor(userId.Value));
        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> SearchAsync(Guid? userId, string q) =>
        GetAsync($"/api/v1/transfers?q={Uri.EscapeDataString(q)}", userId);

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }
}
