using System.Net;
using System.Net.Http.Json;
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
/// T-010-04 (US-010-03 / AC-010-1 blob-sharing clause): the per-user active-bytes expression that T-007-03's quota check
/// and T-007-05's meter both read must count UNIQUE BlobRefs — a 2 GB transfer re-sent N times still counts as 2 GB, not N×2 GB.
/// Facts 1–4 are store-level (seed rows directly, assert on TransferStore); fact 5 drives the real finalize endpoint at the
/// storage limit to prove the quota check reads the SAME unique-BlobRef expression (no fork). Fresh Azurite + SQL 2022 per
/// instance — same pattern as FinalizeAccountLimitsTests.
/// </summary>
public class ResendMeterNoDoubleCountTests : IAsyncLifetime
{
    // Class-level constants — values per seeded flag row, not plan literals (TA-3.4). Reused from FinalizeAccountLimitsTests.
    private const long FreeStorageQuota = 5_368_709_120L;        // 5 GB — migration seed for limits.free.storageQuota / Plan free LimitsJson
    private const string CookieName = "wa.session";
    private const string JwtSecret = "test-jwt-secret";          // pinned in InitializeAsync below — same value the app reads from Wa:Jwt:Secret

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private TransferStore _store = null!;
    private HttpClient _client = null!;
    private Uri _accountUrl = null!;
    private BlobServiceClient _svc = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", _accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", JwtSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        // Store under test — the SAME context, so seeded rows are visible to GetActiveStorageBytesAsync / MarkDeletedAsync.
        _store = new TransferStore(_db);

        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false // TA-9.1 test: Cookie headers are set explicitly per request — the default cookie container would overwrite them
        });
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- AC-010-1 blob-sharing clause: re-send → meter still shows 2 GB, not 4 GB ----

    [Fact]
    public async Task Resend_SharedBlob_MeterCountsOnce_NotDoubleCounted()
    {
        var ownerId = await SeedOwnerAsync();
        const long twoGb = 2_000_000_000L;

        // Original active transfer with a 2 GB blob (RefCount 1).
        var (blobId, _) = await SeedActiveTransferWithBlobAsync(ownerId, twoGb);

        // Re-send: a second active transfer whose FileItem points at the SAME BlobRefId (RefCount → 2).
        await AddResendPointingAtBlobAsync(ownerId, blobId, twoGb);
        await SetRefCountAsync(blobId, 2);

        var bytes = await _store.GetActiveStorageBytesAsync(ownerId, default);
        Assert.Equal(twoGb, bytes); // NOT 4 GB — the unique-BlobRef dedupe is what prevents double-count.
    }

    // ---- Exit check bullet 1: re-sent 10 times → still 2 GB (proves Distinct-by-BlobRefId) ----

    [Fact]
    public async Task ResendTenTimes_MeterStillTwoGB()
    {
        var ownerId = await SeedOwnerAsync();
        const long twoGb = 2_000_000_000L;

        // One original + ten re-sends, all sharing the same BlobRefId.
        var (blobId, _) = await SeedActiveTransferWithBlobAsync(ownerId, twoGb);
        for (var i = 0; i < 10; i++)
            await AddResendPointingAtBlobAsync(ownerId, blobId, twoGb);

        var bytes = await _store.GetActiveStorageBytesAsync(ownerId, default);
        Assert.Equal(twoGb, bytes); // exactly one physical blob — eleven FileItem rows contribute 2 GB once.
    }

    // ---- Exit check bullet 2a: delete original keep re-send → RefCount 1, blob survives (PhysicallyDeletedAtUtc null) ----

    [Fact]
    public async Task DeleteOriginalKeepResend_RefCountOne_BlobSurvives()
    {
        var ownerId = await SeedOwnerAsync();
        const long twoGb = 2_000_000_000L;

        var (blobId, originalId) = await SeedActiveTransferWithBlobAsync(ownerId, twoGb);
        await AddResendPointingAtBlobAsync(ownerId, blobId, twoGb);
        await SetRefCountAsync(blobId, 2);

        // Delete the ORIGINAL via the existing T-009-04 path (soft-delete + RefCount--). The re-send survives.
        var deletedAtUtc = DateTime.UtcNow;
        var result = await _store.MarkDeletedAsync(originalId, ownerId, deletedAtUtc, default);
        Assert.NotNull(result);

        var blob = await GetBlobAsync(blobId);
        Assert.Equal(1, blob.RefCount);                 // 2 → 1 on the original's deletion.
        Assert.Null(blob.PhysicallyDeletedAtUtc);        // still referenced by the re-send — no physical-delete buffer yet.
    }

    // ---- Exit check bullet 2b: delete the re-send too → RefCount 0 + PhysicallyDeletedAtUtc 24 h buffer set ----

    [Fact]
    public async Task DeleteResendToo_RefCountZero_BufferSet()
    {
        var ownerId = await SeedOwnerAsync();
        const long twoGb = 2_000_000_000L;

        // Self-contained state: original + re-send sharing one blob (RefCount 2).
        var (blobId, originalId) = await SeedActiveTransferWithBlobAsync(ownerId, twoGb);
        var resendId = await AddResendPointingAtBlobAsync(ownerId, blobId, twoGb);
        await SetRefCountAsync(blobId, 2);

        // Delete the original first (RefCount → 1), then the re-send (RefCount → 0 + buffer).
        var firstDelete = DateTime.UtcNow;
        await _store.MarkDeletedAsync(originalId, ownerId, firstDelete, default);

        var secondDelete = DateTime.UtcNow.AddSeconds(5);
        var result = await _store.MarkDeletedAsync(resendId, ownerId, secondDelete, default);
        Assert.NotNull(result);

        var blob = await GetBlobAsync(blobId);
        Assert.Equal(0, blob.RefCount);                              // 1 → 0 — no remaining references.
        Assert.Equal(secondDelete.AddHours(24), blob.PhysicallyDeletedAtUtc); // 24 h physical buffer (TA-6.4/6.5).
    }

    // ---- Exit check bullet 3: quota check and meter share ONE expression — at the limit, finalize rejects via it ----

    [Fact]
    public async Task QuotaAndMeterShareExpression_AtLimitNoDisagreement()
    {
        var ownerId = await SeedOwnerAsync();

        // Seed unique blobs (one per transfer) so the shared expression returns a known value V.
        const long seededBytes = FreeStorageQuota - 500L;
        await SeedActiveTransferWithBlobAsync(ownerId, seededBytes);

        // The meter reads exactly V — proving GetActiveStorageBytesAsync is the single source both callers use.
        var v = await _store.GetActiveStorageBytesAsync(ownerId, default);
        Assert.Equal(seededBytes, v);

        // Now finalize a 1024-byte file with a session cookie: activeBytes + totalBytes (V + 1024) exceeds the quota,
        // so the SAME expression rejects it — no fork between what the meter reads and what the quota check enforces.
        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("quota-share.txt");
        using var response = await PostFinalizeAsync(draftId, fileId, "t04-shared-expr", CookieValueFor(ownerId));

        // ProblemCodes pins STORAGE_QUOTA_EXCEEDED at 507 (no HttpStatusCode enum value — compare the int).
        Assert.Equal(507, (int)response.StatusCode);
    }

    // ---- Helpers ----

    /// <summary>Seed an AppUser on the free plan — required because Transfer.OwnerAppUserId has FK_Transfer_Owner.</summary>
    private async Task<Guid> SeedOwnerAsync()
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == ILimitsProvider.GuestPlanCode)
            .Select(p => p.Id).FirstAsync();
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"t04-{Guid.NewGuid():N}@example.com",
            DisplayName = "T-010-04 Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(owner);
        await _db.SaveChangesAsync();
        return owner.Id;
    }

    /// <summary>Seed one active Transfer + its unique BlobRef (RefCount 1) + FileItem in ONE SaveChanges so the new
    /// expression sees real bytes. Returns (blobId, transferId).</summary>
    private async Task<(Guid BlobId, Guid TransferId)> SeedActiveTransferWithBlobAsync(Guid ownerId, long sizeBytes)
    {
        var now = DateTime.UtcNow;
        var blobRef = new BlobRef
        {
            Id = Guid.NewGuid(),
            BlobPath = $"transfers/{Guid.NewGuid():N}/{Guid.NewGuid():N}",
            SizeBytes = sizeBytes,
            RefCount = 1,
            CreatedAtUtc = now
        };
        _db.Set<BlobRef>().Add(blobRef);

        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = wa.domain.ValueObjects.LinkId.Mint().Value,
            Status = 1, // Active — inside GetActiveStorageBytesAsync WHERE (Status IN (1,3)).
            OwnerAppUserId = ownerId,
            TotalBytes = sizeBytes,
            FileCount = 1,
            SenderName = "T-010-04",
            MaxDownloads = 1,
            ExpiresAtUtc = now.AddDays(30),
            CreatedAtUtc = now
        });

        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobRef.Id,
            OriginalName = "seeded.txt",
            SizeBytes = sizeBytes,
            SortOrder = 0
        });

        await _db.SaveChangesAsync();
        return (blobRef.Id, transferId);
    }

    /// <summary>Add a re-send: one more active Transfer whose FileItem points at an EXISTING BlobRefId (no new blob).
    /// Returns the new transfer's Id. RefCount is set separately via SetRefCountAsync.</summary>
    private async Task<Guid> AddResendPointingAtBlobAsync(Guid ownerId, Guid blobId, long sizeBytes)
    {
        var now = DateTime.UtcNow;
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = wa.domain.ValueObjects.LinkId.Mint().Value,
            Status = 1,
            OwnerAppUserId = ownerId,
            TotalBytes = sizeBytes,
            FileCount = 1,
            SenderName = "T-010-04 Resend",
            MaxDownloads = 1,
            ExpiresAtUtc = now.AddDays(30),
            CreatedAtUtc = now
        });

        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobId, // SAME physical blob — the re-send shares it.
            OriginalName = "seeded.txt",
            SizeBytes = sizeBytes,
            SortOrder = 0
        });

        await _db.SaveChangesAsync();
        return transferId;
    }

    /// <summary>Set a BlobRef's RefCount directly (the expression sums distinct blobs regardless of RefCount — this only
    /// models the refcount state facts 3/4 assert against via MarkDeletedAsync).</summary>
    private async Task SetRefCountAsync(Guid blobId, int refCount)
    {
        var blob = await _db.Set<BlobRef>().FirstAsync(b => b.Id == blobId);
        blob.RefCount = refCount;
        await _db.SaveChangesAsync();
    }

    /// <summary>Read a BlobRef's committed state (AsNoTracking — fresh from DB, not the tracked instance).</summary>
    private async Task<BlobRef> GetBlobAsync(Guid blobId) =>
        await _db.Set<BlobRef>().AsNoTracking().FirstAsync(b => b.Id == blobId);

    /// <summary>TA-9.1 provisional scheme: Base64(userId|exp|sig), sig = HMAC-SHA256(userId|exp, Wa:Jwt:Secret).</summary>
    private static string CookieValueFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(JwtSecret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{userId:N}|{exp}")));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId:N}|{exp}|{sig}"));
    }

    private async Task<(Guid DraftId, Guid FileId)> SetupDraftAndStagedBlobAsync(string name)
    {
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = new[] { new { name, sizeBytes = 1024L, contentType = "text/plain" } } });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        var bytes = new byte[1024];
        RandomNumberGenerator.Fill(bytes);
        var path = BlobPaths.StagingFile(draftId, fileId);
        var cut = path.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(path[..cut]).GetBlobClient(path[(cut + 1)..])
                .UploadAsync(ms, true, default);

        return (draftId, fileId);
    }

    private async Task<HttpResponseMessage> PostFinalizeAsync(Guid draftId, Guid fileId, string idempotencyKey, string? sessionCookie)
    {
        var payload = JsonSerializer.Serialize(new { files = new[] { new { fileId, name = "x.txt", sizeBytes = 1024L, contentType = "text/plain", retries = 0 } } });
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (sessionCookie is not null)
            request.Headers.TryAddWithoutValidation("Cookie", $"{CookieName}={sessionCookie}");
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
