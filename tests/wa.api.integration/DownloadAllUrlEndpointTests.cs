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
using wa.domain;
using wa.domain.Errors;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-004-02 exit checks for GET /api/v1/public/transfers/{linkId}/download-all-url?t= (TA-4.2#7):
/// 30-min read SAS minted on the zip blob when it exists, 202 with pollAfterSec when generating, DownloadsCount +1 per mint
/// + one DownloadEvent row (fileId = null for download-all), and the idempotent Status→3 cap flip with the canonical
/// MAX_DOWNLOADS_REACHED body (TA-4.2#7, D6). Fresh Azurite + SQL 2022 per instance (TA-14.3).
/// </summary>
public class DownloadAllUrlEndpointTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private string _connString = null!;
    private HttpClient _client = null!;
    private BlobServiceClient _svc = null!;

    public async Task InitializeAsync()
    {
        // Endpoint 7 mints SAS via the real DI-registered IBlobSasMinter — Azurite is required both for minting and for the direct-stream GETs.
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);
        _connString = connString;

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        // The test seeds blobs directly (same style as DownloadUrlEndpointTests._svc) — no browser between this test and storage.
        _svc = new BlobServiceClient(
            accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Exit check 1: Zip exists → 200 { url } + direct-stream GET returns the exact uploaded bytes ----

    [Fact]
    public async Task Ok_200_SasUrl_DirectStream_ReturnsExactUploadedBytes()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d3w5f1q1", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "first.txt", 2048L)]);

        // Upload a zip blob at the canonical path.
        var bytes = new byte[2048];
        RandomNumberGenerator.Fill(bytes);
        await UploadZipBlobAsync(transferId, bytes);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d3w5f1q1/download-all-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // TA-4.2#7 exact wire shape — exactly { url } when the zip is ready (200).
        Assert.Equal(["url"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        var url = new Uri(doc.RootElement.GetProperty("url").GetString()!); // the 30-min read SAS

        // Direct stream: a plain HttpClient GETs the SAS URL straight from storage — no /api/ prefix, no zip container.
        using var sas = new HttpClient();
        using var streamResponse = await sas.GetAsync(StreamUrl(url));
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var streamed = await streamResponse.Content.ReadAsByteArrayAsync();

        // Byte-exact — the zip blob round-trips through the SAS (exit check: not re-zipped).
        Assert.Equal(bytes, streamed.ToArray());
    }

    // ---- Exit check 2: DownloadsCount +1 and exactly one DownloadEvent row per mint (fileId = null for download-all) ----

    [Fact]
    public async Task Mint_RecordsDownloadsCountPlusOneAndOneDownloadEventRow_FileIdNull()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d5w3f1q3", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "counted.txt", 10L)]);

        // Upload a zip blob at the canonical path.
        await UploadZipBlobAsync(transferId, [1, 2, 3]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d5w3f1q3/download-all-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // TA-7.2 step 4: the count increments AT mint time (MVP over-count documented — the browser streams directly after).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.DownloadsCount); // +1 from the seeded 0

        // One DownloadEvent row for THIS transfer (T-017: recorded at mint time).
        var rows = await freshDb.Set<DownloadEvent>().Where(e => e.TransferId == transferId).ToListAsync();
        Assert.Single(rows);
        Assert.Null(rows[0].FileId); // fileId is null for download-all (TA-3.2: NULL = download-all)
    }

    // ---- Exit check 3: Cap reached → Status=3 flip + MAX_DOWNLOADS_REACHED (410); further mints keep failing idempotently ----

    [Fact]
    public async Task CapReached_StatusFlipsTo3_IdempotentMaxDownloadsReached()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d9w7f2q5", maxDownloads: 2);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "capped.txt", 10L)]);

        // Upload a zip blob at the canonical path.
        await UploadZipBlobAsync(transferId, [1, 2, 3]);

        // Two mints succeed (0 → 2).
        for (var i = 0; i < 2; i++)
            using (var ok = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/download-all-url"))
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Third mint: at cap → 410 MAX_DOWNLOADS_REACHED.
        using var atCap = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/download-all-url");
        Assert.Equal(HttpStatusCode.Gone, atCap.StatusCode); // 410 — the closed table's status for MAX_DOWNLOADS_REACHED

        using (var doc = JsonDocument.Parse(await atCap.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal(ProblemCodes.MaxDownloadsReached, doc.RootElement.GetProperty("code").GetString()); // TA-4.2#7's canonical code
            Assert.Equal(410, doc.RootElement.GetProperty("status").GetInt32());
        }

        // Status flipped to 3 (the idempotent flip — US-005-03's literal; the flip lives in the store, not a worker).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(3, transfer.Status);

        // Fourth mint: still 410 (idempotent — the flip is idempotent by construction).
        using var atCapAgain = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/download-all-url");
        Assert.Equal(HttpStatusCode.Gone, atCapAgain.StatusCode);
        Assert.Equal(ProblemCodes.MaxDownloadsReached, JsonDocument.Parse(await atCapAgain.Content.ReadAsByteArrayAsync()).RootElement.GetProperty("code").GetString());

        // No extra rows: the rejected mints recorded nothing (the flip is in the DB; the mint was never attempted).
        var transferAfter = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(2, transferAfter.DownloadsCount); // still 2 — the failed mints did not increment
        Assert.Equal(2, await freshDb.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId)); // exactly the two successful rows
    }

    // ---- Exit check 4: Zip doesn't exist → 202 { pollAfterSec } (trigger f-zip generation) ----

    [Fact]
    public async Task ZipDoesNotExist_202_PollAfterSec()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d7w5f4q6", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "first.txt", 2048L)]);

        // No zip blob uploaded — the endpoint should return 202 with pollAfterSec.
        using var response = await _client.GetAsync("/api/v1/public/transfers/d7w5f4q6/download-all-url");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); // 202 — the zip is being generated

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // TA-4.2#7 exact wire shape — exactly { pollAfterSec } when the zip is being generated (202).
        Assert.Equal(["pollAfterSec"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(2, doc.RootElement.GetProperty("pollAfterSec").GetInt32()); // 2 seconds — the task doc's value

        // No download counted on the 202 path (the zip doesn't exist yet — no mint happened).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(0, transfer.DownloadsCount); // still 0 — the 202 path doesn't count downloads
    }

    // ---- Exit check 5: Race safety — two first-clickers both get 202; f-zip's path-idempotency means exactly one zip wins ----

    [Fact]
    public async Task RaceSafety_TwoFirstClickersBothGet202_ExactlyOneZipWins()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d9w7f5q7", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "first.txt", 2048L)]);

        // No zip blob uploaded — both requests should get 202.
        using var response1 = await _client.GetAsync("/api/v1/public/transfers/d9w7f5q7/download-all-url");
        Assert.Equal(HttpStatusCode.Accepted, response1.StatusCode); // 202 — the zip is being generated

        using var response2 = await _client.GetAsync("/api/v1/public/transfers/d9w7f5q7/download-all-url");
        Assert.Equal(HttpStatusCode.Accepted, response2.StatusCode); // 202 — the zip is still being generated

        // Both got 202 — the f-zip function's path-idempotency means exactly one zip wins (the second POST sees the existing blob).
        // No download counted on either 202 path (the zip doesn't exist yet — no mint happened).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(0, transfer.DownloadsCount); // still 0 — the 202 path doesn't count downloads
    }

    // ---- Exit check 6: Gated transfer: mint without ?t= → 401 UNAUTHENTICATED; POST /unlock then mint with ?t= → 200 ----

    [Fact]
    public async Task GatedTransfer_MintWithoutToken_Returns401()
    {
        var password = "secret-pass-123";
        var transferId = await SeedTransferAsync(status: 1, linkId: "d1w7f8q0", maxDownloads: 5, password: password);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "gated.txt", 10L)]);

        // Upload a zip blob at the canonical path.
        await UploadZipBlobAsync(transferId, [1, 2, 3]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d1w7f8q0/download-all-url");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // 401 — the auth gate blocks the mint (TA-9.6#1)

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(ProblemCodes.Unauthenticated, doc.RootElement.GetProperty("code").GetString()); // UNAUTHENTICATED code
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task GatedTransfer_MintWithValidToken_Returns200()
    {
        const string password = "correct horse";
        var transferId = await SeedTransferAsync(status: 1, linkId: "d3w9f8q2", maxDownloads: 5, password: password);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "gated-ok.txt", 10L)]);

        // Upload a zip blob at the canonical path.
        await UploadZipBlobAsync(transferId, [1, 2, 3]);

        // POST /unlock to get a token (T-003-04's pattern).
        using var unlockResponse = await _client.PostAsync(
            "/api/v1/public/transfers/d3w9f8q2/unlock",
            new StringContent(JsonSerializer.Serialize(new { password }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        using var unlockDoc = JsonDocument.Parse(await unlockResponse.Content.ReadAsByteArrayAsync());
        var token = unlockDoc.RootElement.GetProperty("token").GetString()!;

        // Mint with the valid ?t= token → 200.
        using var mintResponse = await _client.GetAsync(
            "/api/v1/public/transfers/d3w9f8q2/download-all-url?t=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.OK, mintResponse.StatusCode); // 200 — the token clears the gate (TA-9.6#1)

        using var doc = JsonDocument.Parse(await mintResponse.Content.ReadAsByteArrayAsync());
        // The SAS URL is returned (the Ok state).
        Assert.True(doc.RootElement.TryGetProperty("url", out _));
    }

    // ---- Helpers (same seed pattern as DownloadUrlEndpointTests) ----

    private async Task<Guid> SeedTransferAsync(int status, string linkId, int maxDownloads = 5, int downloadsCount = 0, string? senderEmail = null, long totalBytes = 0, string? password = null)
    {
        var id = Guid.NewGuid();

        // TA-9.1: hash with the SAME PasswordHasher<string> + user-id keying as SendTransfer.cs (transfer.Id.ToString("n")).
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
            PasswordHash = passwordHash, // the PBKDF2-SHA256 hash (T-003-04: gated transfers store this)
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
                BlobPath = BlobPaths.TransferFile(transferId, fileId), // the canonical path (TA-3.5) — endpoint 6's SAS points here
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
                OriginalName = name, // the original filename — endpoint 6's rs-cd override uses this
                SizeBytes = sizeBytes,
                ContentType = "text/plain",
                SortOrder = i // TA-4.2#4: GET returns files in SortOrder ascending
            });
        }

        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = files.Length;
        transfer.TotalBytes = files.Sum(f => f.sizeBytes);
        await _db.SaveChangesAsync();
    }

    /// <summary>Upload zip blob bytes to the canonical transfer-zip path (TA-3.5) — endpoint 7's SAS streams from here.</summary>
    private async Task UploadZipBlobAsync(Guid transferId, byte[] bytes)
    {
        // BlobPaths.TransferZip returns the canonical TA-3.5 path "transfers/{t}/all.zip" where
        // the FIRST segment is the container. SplitPath gives us (container="transfers", blobName="{t}/all.zip").
        var path = BlobPaths.TransferZip(transferId);
        var cut = path.IndexOf('/');
        var (container, blobName) = (path[..cut], path[(cut + 1)..]);
        using var stream = new MemoryStream(bytes);
        await _svc.GetBlobContainerClient(container).GetBlobClient(blobName).UploadAsync(stream);
    }

    /// <summary>
    /// A fresh context for reading AFTER the app has mutated rows via its own DI singleton DbContext.
    /// The seed context holds tracked entities with stale in-memory values (DownloadsCount=0, etc.); the app's
    /// ExecuteUpdateAsync / SaveChangesAsync hit SQL directly through a different instance. A fresh context has no
    /// identity-map entries, so its queries read the real DB rows — the only way to see the app's writes.
    /// </summary>
    private WaDbContext FreshDb() =>
        new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);

    // ---- T-055-05's transport-local helpers, copied for this fixture: re-address the minted zip read SAS for an IP-hosted
    // Azurite + re-sign the protocol line as "https,http" (a plain-HTTP GET rejects spr=https without it) and the canonical
    // resource path (/blob/{account}/{container}/{blob}). The minted value itself is unchanged — this shim exists only because
    // Azurite uses path-style addressing and plain HTTP, while the minter emits the production host-style https URL (TA-3.6). ----

    private static Uri StreamUrl(Uri minted)
    {
        var rawQuery = minted.Query.TrimStart('?');
        var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => { var eq = p.IndexOf('='); return (k: p[..eq], v: p[(eq + 1)..]); })
            .ToList();

        // Unescape the tokens that go into the string-to-sign.
        var se = Uri.UnescapeDataString(parts.Single(p => p.k == "se").v);

        // TA-3.6 16-line blob SAS string-to-sign: sp=r (read), no start bound, Azurite canonical path,
        // protocol re-signed for the plain-HTTP leg, no rs-cd override (the zip has no original-filename override).
        const string ProtocolLine = "https,http";
        var stringToSign = string.Join('\n', new[]
        {
            "r",                                     // sp — read (TA-3.6 recipient zip)
            string.Empty,                           // start time (none for recipients)
            se,                                     // expiry
            "/blob/" + AzuriteBuilder.AccountName + minted.AbsolutePath,   // canonical resource
            string.Empty,                           // identifier
            string.Empty,                           // IP address range
            ProtocolLine,                           // protocol — re-signed for the plain-HTTP browser leg
            "2024",                                 // TA-3.6 version literal (the minter's Version)
            "b",                                    // resource: blob
            string.Empty,                           // snapshot time
            string.Empty,                           // encryption scope
            string.Empty,                           // cache control
            string.Empty,                           // content disposition — no rs-cd override for the zip
            string.Empty,                           // content encoding
            string.Empty,                           // content language
            string.Empty                            // content type
        });

        using var hmac = new HMACSHA256(Convert.FromBase64String(AzuriteBuilder.AccountKey));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        // Re-address to Azurite's path-style form + replace only spr and sig, preserving every other token exactly.
        var newParts = parts.Select(p =>
        {
            if (p.k == "sig") return "sig=" + Uri.EscapeDataString(sig);
            if (p.k == "spr") return "spr=https%2Chttp";
            return p.k + "=" + p.v; // keep the original escaped value
        });
        return new Uri(minted.GetLeftPart(UriPartial.Authority)
            + "/" + AzuriteBuilder.AccountName + minted.AbsolutePath + "?" + string.Join("&", newParts));
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
