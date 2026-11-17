using System.Diagnostics;
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
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-007-06 / FR-007-5 exit checks: a flag override changes limits.free.maxTransferSize without deploy —
/// a 6 GB draft is rejected at the seeded 5 GB limit with 422 TRANSFER_SIZE_EXCEEDED, and after upserting the
/// FeatureFlag row to 10 GB + waiting out the REAL 30 s cache TTL (TA-3.4), an identical new draft is accepted;
/// EC-007-1: an already-finalized transfer keeps its send-time values — only new resolutions pick up the change.
/// Asserts via HTTP only (draft/send endpoints); DB used for the flag upsert + post-hoc assertions (TA-14.3).
/// </summary>
public class FlagOverrideIntegrationTests : IAsyncLifetime
{
    // Class-level constants — values per seeded flag row, not plan literals (TA-3.4).
    private const long FreeMaxTransferSize = 5_368_709_120L;   // 5 GB — migration seed for limits.free.maxTransferSize
    private const long TenGb = 10_737_418_240L;                // 10 GB — the override value written by the tests
    private const long SixGb = 6_442_450_944L;                 // 6 GB — over 5 GB, under 10 GB: single-file payload that isolates MAX_TRANSFER_SIZE

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;
    private Uri _accountUrl = null!;
    private BlobServiceClient _svc = null!;
    private string _connString = null!; // kept for the flag upsert — see UpsertFlagAsync.

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", _accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");
        // TA-4.1.7 / EC-054-1: outbox rows commit via their own AppDbContextFactory context before broker publish;
        // a dummy (unreachable) connection string exercises that path deterministically.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + _accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", _connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        // MigrateAsync seeds the 5 GB flag rows via migration 20261011215315_SeedPlansAndFeatureFlags.
        await _db.Database.MigrateAsync();

        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- FR-007-5: 5 GB flag rejects 6 GB; after upsert to 10 GB + real 30 s TTL wait, a new 6 GB draft is accepted ----

    [Fact]
    public async Task Flag_Override_Draft_RejectedAt5GB_AcceptedAfter10GB_NoDeploy()
    {
        // Fixture guard: the migration must have seeded the flag row at exactly "5368709120" (5 GB).
        var seeded = await _db.Set<FeatureFlag>().FirstAsync(f => f.Key == "limits.free.maxTransferSize");
        Assert.Equal("5368709120", seeded.Value);

        // Time from the request that warms FlagsCache — the cache entry is stamped on resolution, so the
        // next draft must not resolve earlier than 30 s after this one.
        var sw = Stopwatch.StartNew();

        // Rejected at the seeded 5 GB limit.
        using var rejected = await PostDraftAsync(SixGbPayload());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        using (var doc = JsonDocument.Parse(await rejected.Content.ReadAsByteArrayAsync()))
        {
            var root = doc.RootElement;
            // TA-4.1.3 closed list: TRANSFER_SIZE_EXCEEDED pinned at 422 — status field agrees with the HTTP status.
            Assert.Equal("TRANSFER_SIZE_EXCEEDED", root.GetProperty("code").GetString());
            Assert.Equal(422, root.GetProperty("status").GetInt32());

            // AC-007-1-style shape: details contain both numbers — actual size and limit (AC-007-1).
            var details = root.GetProperty("details").GetString();
            Assert.NotNull(details);
            double actualGib = SixGb / 1073741824.0;          // 6.0 GB
            double limitGib = FreeMaxTransferSize / 1073741824.0; // 5.0 GB
            Assert.Contains(actualGib.ToString("F1") + " GB", details);
            Assert.Contains(limitGib.ToString("F1") + " GB", details);
        }

        // Upsert the flag to 10 GB (admin endpoint 23 arrives later — F-TRF-011/T-024 replaces this).
        // Both MAX_TRANSFER_SIZE and MAX_SINGLE_FILE move together: the seeded maxSingleFile (5 GB) would otherwise
        // keep rejecting a single 6 GB file — AC-007-1's details shape names the single-file limit for file violations.
        const string NewValue = "10737418240";
        foreach (var key in new[] { "limits.free.maxTransferSize", "limits.free.maxSingleFile" })
        {
            var storedValue = await UpsertFlagAsync(key, NewValue);
            Assert.Equal(NewValue, storedValue);
        }

        // Wait out the REAL 30 s cache TTL (TA-3.4) — no env override, no fake clock; ~31 s so elapsed is ≥ 30 s.
        await Task.Delay(31_000);
        Assert.True(sw.Elapsed.TotalSeconds >= 30.0, "Real TTL wait must be ≥ 30 s (TA-3.4) — do not shorten it");

        // New identical draft → accepted at the new limit, no deploy in between.
        using var accepted = await PostDraftAsync(SixGbPayload());
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        using (var doc = JsonDocument.Parse(await accepted.Content.ReadAsByteArrayAsync()))
        {
            var root = doc.RootElement;
            // TA-4.2#1 shape: top-level draftId / expiresInSec / files, one file row for the single-file payload.
            Assert.NotNull(root.GetProperty("draftId").GetString());
            Assert.Equal(7200, root.GetProperty("expiresInSec").GetInt32());
            var files = root.GetProperty("files");
            Assert.Equal(1, files.GetArrayLength());
        }
    }

    // ---- EC-007-1: in-flight (already-finalized) transfers keep their send-time values after a flag edit ----

    [Fact]
    public async Task In_Flight_Transfer_KeepsSendTimeValues_AfterFlagEdit()
    {
        // Finalize a small transfer UNDER the seeded 5 GB flag — before any edit.
        var transferId = await SetupFinalizedTransferAsync();

        const string NewValue = "10737418240"; // 10 GB
        var sw = Stopwatch.StartNew();
        var storedValue = await UpsertFlagAsync("limits.free.maxTransferSize", NewValue);
        Assert.Equal(NewValue, storedValue);

        // Wait out the REAL 30 s TTL (TA-3.4), timed from the finalized send's cache warm-up above.
        await Task.Delay(31_000);
        Assert.True(sw.Elapsed.TotalSeconds >= 30.0, "Real TTL wait must be ≥ 30 s (TA-3.4) — do not shorten it");

        // Send AFTER the edit → succeeds with its OWN send-time values; only new resolutions pick up the change.
        using var response = await PostSendAsync(transferId, new { emails = new[] { "a@x.com" } }, $"key-flag-inflight-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("Active", root.GetProperty("status").GetString());

        // Post-hoc DB assertions: Status 0→1 and TotalBytes unchanged (send-time values kept).
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.Status);
        Assert.Equal(512L, transfer.TotalBytes);
    }

    // ---- Helpers ----

    private static (string Name, long SizeBytes)[] SixGbPayload() =>
        [("big.mp4", SixGb)];

    /// <summary>
    /// Upsert the FeatureFlag row (Key is the PK). Writes through its own WaDbContext instance and reads back
    /// through a fresh one so the returned value is provably from the database, not an in-memory write.
    /// NOTE: this does NOT solve FR-007-5 — see the escalated defect: the API's singleton WaDbContext (Program.cs)
    /// tracks FeatureFlag rows after the first draft request, and EF's identity map then keeps serving those
    /// tracked entities on every subsequent FlagsStore read — the 30 s FlagsCache TTL expires, but the app still
    /// sees the pre-edit values until restart. The integration test fails exactly there.
    /// </summary>
    private async Task<string> UpsertFlagAsync(string key, string value)
    {
        using var upsertDb = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var flag = await upsertDb.Set<FeatureFlag>().FindAsync(key);
        if (flag == null)
        {
            upsertDb.Set<FeatureFlag>().Add(new FeatureFlag
            {
                Key = key,
                Value = value,
                Description = "T-007-06 integration test override",
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            flag.Value = value;
            flag.UpdatedAtUtc = DateTime.UtcNow;
        }
        await upsertDb.SaveChangesAsync();

        using var readDb = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        return (await readDb.Set<FeatureFlag>().FirstAsync(f => f.Key == key)).Value;
    }

    /// <summary>POST /api/v1/transfers/draft (TA-4.2#1) — caller asserts status + shape.</summary>
    private async Task<HttpResponseMessage> PostDraftAsync((string Name, long SizeBytes)[] files)
    {
        var payload = new
        {
            files = files.Select(f => new { name = f.Name, sizeBytes = f.SizeBytes, contentType = (string?)null })
        };
        return await _client.PostAsJsonAsync("/api/v1/transfers/draft", payload);
    }

    /// <summary>Draft → staged-blob upload → finalize. Returns the finalized transfer id.</summary>
    private async Task<Guid> SetupFinalizedTransferAsync()
    {
        var draftResponse = await PostDraftAsync([("test.txt", 512L)]);
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        // Upload a blob to staging — canonical TA-3.5 split (TA-3.5): first path segment is the container, rest the blob.
        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        var bytes = new byte[512];
        RandomNumberGenerator.Fill(bytes);
        var cut = stagingPath.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(stagingPath[..cut]).GetBlobClient(stagingPath[(cut + 1)..])
                .UploadAsync(ms, true, default);

        var finalizePayload = JsonSerializer.Serialize(new { files = new[] { new { fileId, name = "test.txt", sizeBytes = 512L, contentType = "text/plain", retries = 0 } } });
        var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(finalizePayload, Encoding.UTF8, "application/json")
        };
        finalizeRequest.Headers.Add("Idempotency-Key", $"key-finalize-{Guid.NewGuid():N}");
        using var finalizeResponse = await _client.SendAsync(finalizeRequest);
        Assert.Equal(HttpStatusCode.Created, finalizeResponse.StatusCode);

        using var finalizeDoc = JsonDocument.Parse(await finalizeResponse.Content.ReadAsByteArrayAsync());
        return new Guid(finalizeDoc.RootElement.GetProperty("id").GetString()!);
    }

    private async Task<HttpResponseMessage> PostSendAsync(Guid transferId, object body, string idempotencyKey)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/{transferId:N}/send")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
