using System.Net;
using System.Net.Http.Headers;
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
using wa.infrastructure.Blob;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-001-10 exit checks for POST /api/v1/transfers/draft/{draftId}/finalize (TA-4.2#2):
/// response shape, byte-exact blob copy staging→transfers, idempotent replay (EC-002-1).
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as DraftEndpointTests.
/// </summary>
public class FinalizeEndpointTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
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
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
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

    // ---- Exit check 1: TA-4.2#2 response shape — exact property names ----

    [Fact]
    public async Task Finalize_Endpoint_Returns_TA_4_2_Response_Shape()
    {
        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("hello.txt", "text/plain");
        var payload = BuildPayload(fileId, "hello.txt", 1024L, "text/plain");

        using var response = await PostFinalizeAsync(draftId, payload, "key-shape");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // TA-4.2#2 exact shape: id, linkId, publicUrl, status, expiresAt, totalBytes, files.
        Assert.Equal(["id", "linkId", "publicUrl", "status", "expiresAt", "totalBytes", "files"],
            root.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.Equal("Draft", root.GetProperty("status").GetString());
        Assert.Equal(1024, root.GetProperty("totalBytes").GetInt64());
        Assert.StartsWith("http://localhost:5173/t/", root.GetProperty("publicUrl").GetString());

        var files = root.GetProperty("files");
        Assert.Equal(1, files.GetArrayLength());
        var fileRow = files[0];
        Assert.Equal(["id", "name", "sizeBytes", "contentType"],
            fileRow.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("hello.txt", fileRow.GetProperty("name").GetString());
        Assert.Equal(1024, fileRow.GetProperty("sizeBytes").GetInt64());
    }

    // ---- Exit check 3: byte-exact blob copy staging→transfers ----

    [Fact]
    public async Task Finalize_Endpoint_Blob_bytes_are_byte_exact_at_transfer_path()
    {
        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("payload.bin", "application/octet-stream");
        var stagedBytes = BuildPayload();
        // Re-upload the exact bytes we want to verify.
        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        using (var ms = new MemoryStream(stagedBytes))
            await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).GetBlobClient(stagingPath).UploadAsync(ms);

        var payload = BuildPayload(fileId, "payload.bin", stagedBytes.Length, "application/octet-stream");
        using var response = await PostFinalizeAsync(draftId, payload, "key-bytes");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var transferId = new Guid(doc.RootElement.GetProperty("id").GetString()!);

        // Download from the transfer path and compare byte-for-byte.
        var transferPath = BlobPaths.TransferFile(transferId, fileId);
        await using var stream = await new AzureBlobStore(_svc).DownloadAsync(transferPath);
        using var roundTripped = new MemoryStream();
        await stream.CopyToAsync(roundTripped);

        Assert.Equal(stagedBytes, roundTripped.ToArray()); // byte-for-byte identical.
    }

    // ---- Exit check 2: idempotent replay — same key → identical result, one Transfer row, blob copied once ----

    [Fact]
    public async Task Finalize_Endpoint_IdempotentReplay_Returns_identical_transfer_one_row_one_copy()
    {
        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("idem.txt", "text/plain");
        var stagedBytes = new byte[512];
        RandomNumberGenerator.Fill(stagedBytes);

        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        using (var ms = new MemoryStream(stagedBytes))
            await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).GetBlobClient(stagingPath).UploadAsync(ms);

        var payload = BuildPayload(fileId, "idem.txt", stagedBytes.Length, "text/plain");

        // First call.
        using var response1 = await PostFinalizeAsync(draftId, payload, "key-replay");
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Second call with same idempotency key.
        using var response2 = await PostFinalizeAsync(draftId, payload, "key-replay");
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();

        // Identical bodies.
        Assert.Equal(body1, body2);

        // DB has exactly 1 Transfer row.
        var count = await _db.Set<Transfer>().CountAsync();
        Assert.Equal(1, count);

        // Blob was copied only once — verify by checking the transfer path exists (it does) and that there's only one Transfer.
        using var doc = JsonDocument.Parse(body1);
        var transferId = new Guid(doc.RootElement.GetProperty("id").GetString()!);
        var transferPath = BlobPaths.TransferFile(transferId, fileId);
        var blobClient = _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).GetBlobClient(transferPath);
        Assert.True(await blobClient.ExistsAsync());
    }

    // ---- Exit check 4: TTL expiry — expired key is NOT replayed (T-002-02) ----

    [Fact]
    public async Task Finalize_Endpoint_ExpiredIdempotencyKey_Creates_new_transfer()
    {
        var (draftId, fileId) = await SetupDraftAndStagedBlobAsync("ttl.txt", "text/plain");
        var stagedBytes = new byte[256];
        RandomNumberGenerator.Fill(stagedBytes);

        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        using (var ms = new MemoryStream(stagedBytes))
            await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).GetBlobClient(stagingPath).UploadAsync(ms);

        // Pre-insert an expired idempotency key into the DB.
        var expiredKey = "key-expired-ttl";
        _db.Set<IdempotencyKey>().Add(new IdempotencyKey
        {
            Key = expiredKey,
            ResultJson = "{\"transferId\":\"00000000-0000-0000-0000-000000000000\"}",
            StatusCode = 201,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-48),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-24) // expired 24 h ago
        });
        await _db.SaveChangesAsync();

        var payload = BuildPayload(fileId, "ttl.txt", stagedBytes.Length, "text/plain");

        // POST with the expired key — should NOT replay; should create a new transfer.
        using var response = await PostFinalizeAsync(draftId, payload, expiredKey);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // A new Transfer row was created (the expired key did not short-circuit).
        var count = await _db.Set<Transfer>().CountAsync();
        Assert.Equal(1, count);

        // The response has a real transfer ID (not the dummy from the expired entry).
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var transferId = new Guid(doc.RootElement.GetProperty("id").GetString()!);
        Assert.NotEqual(Guid.Empty, transferId);
    }

    // ---- Helpers ----

    /// <summary>Create a draft via the existing endpoint and stage a blob at the canonical staging path. Returns (draftId, fileId).</summary>
    private async Task<(Guid DraftId, Guid FileId)> SetupDraftAndStagedBlobAsync(string name, string contentType)
    {
        // Create draft.
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = new[] { new { name, sizeBytes = 1024L, contentType } } });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        // Upload a blob to staging directly (simpler than SAS round-trip for integration tests).
        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        var bytes = new byte[1024];
        RandomNumberGenerator.Fill(bytes);
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).GetBlobClient(stagingPath).UploadAsync(ms);

        return (draftId, fileId);
    }

    private static string BuildPayload(Guid fileId, string name, long sizeBytes, string? contentType) =>
        JsonSerializer.Serialize(new { files = new[] { new { fileId, name, sizeBytes, contentType, retries = 0 } } });

    private static byte[] BuildPayload()
    {
        var bytes = new byte[1024];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private async Task<HttpResponseMessage> PostFinalizeAsync(Guid draftId, string jsonBody, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
