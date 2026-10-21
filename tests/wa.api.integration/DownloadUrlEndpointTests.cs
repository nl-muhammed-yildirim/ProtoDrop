using System.Net;
using System.Net.Http.Headers;
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
/// T-003-06 exit checks for GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url (TA-4.2#6, TA-7.2 step 4):
/// 30-min read SAS minted on the file blob (browser streams directly — no server proxying), DownloadsCount +1 per mint + one
/// DownloadEvent row, the idempotent Status→3 cap flip with the canonical MAX_DOWNLOADS_REACHED body (TA-4.2#6, D6), and the
/// RFC 5987 Content-Disposition override (EC-003-3) round-tripping through real Azurite.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as PublicTransferEndpointTests/FinalizeEndpointTests.
/// </summary>
public class DownloadUrlEndpointTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private string _connString = null!;
    private HttpClient _client = null!;
    private BlobServiceClient _svc = null!;

    public async Task InitializeAsync()
    {
        // Endpoint 6 mints SAS via the real DI-registered IBlobSasMinter — Azurite is required both for minting and for the direct-stream GETs.
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

        // The test seeds blobs directly (same style as FinalizeEndpointTests._svc) — no browser between this test and storage.
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

    // ---- Exit check 1: 200 { url, expiresAt } + direct-stream GET returns the exact uploaded bytes (no zip) ----

    [Fact]
    public async Task Ok_200_SasUrl_DirectStream_ReturnsExactUploadedBytes()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d3w5f1q1", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "first.txt", 2048L)]);

        // The canonical upload — the exact bytes endpoint 6's SAS must stream back (direct storage GET, no server proxying).
        var bytes = new byte[2048];
        RandomNumberGenerator.Fill(bytes);
        await UploadBlobAsync(transferId, fileId, bytes);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d3w5f1q1/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // TA-4.2#6 exact wire shape — exactly these two properties (02-feature-plan §F-TRF-003 pinned).
        Assert.Equal(["url", "expiresAt"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        var url = new Uri(doc.RootElement.GetProperty("url").GetString()!); // the 30-min read SAS
        var expiresAt = DateTimeOffset.Parse(doc.RootElement.GetProperty("expiresAt").GetString()!); // ≈ now + 30 min (TA-3.6)

        Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31)); // 30-min window (T-003-06)

        // Direct stream: a plain HttpClient GETs the SAS URL straight from storage — no /api/ prefix, no zip container.
        // StreamUrl re-addresses for IP-hosted Azurite + plain HTTP (same T-055-05 shim as DraftEndpointTests); the minted value is unchanged.
        using var sas = new HttpClient();
        using var streamResponse = await sas.GetAsync(StreamUrl(url));
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var streamed = await streamResponse.Content.ReadAsByteArrayAsync();

        // Byte-exact — the transfer-file blob round-trips through the SAS (exit check: not zipped).
        Assert.Equal(bytes, streamed.ToArray());
    }

    // ---- Exit check 2: DownloadsCount +1 and exactly one DownloadEvent row per mint ----

    [Fact]
    public async Task Mint_RecordsDownloadsCountPlusOneAndOneDownloadEventRow()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d5w3f1q3", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "counted.txt", 10L)]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d5w3f1q3/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // TA-7.2 step 4: the count increments AT mint time (MVP over-count documented — the browser streams directly after).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.DownloadsCount); // +1 from the seeded 0

        // One DownloadEvent row for THIS file (T-016: recorded at mint time).
        var rows = await freshDb.Set<DownloadEvent>().Where(e => e.TransferId == transferId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(fileId, rows[0].FileId); // the fileId is stored (download-all rows would be null — this is single-file)
    }

    // ---- Exit check 2: re-download within cap works (MaxDownloads > 1) ----

    [Fact]
    public async Task ReDownload_WithinCap_StillWorks_CountsEachMint()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d7w5f1q4", maxDownloads: 3);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "again.txt", 10L)]);

        for (var i = 0; i < 2; i++)
        {
            using var response = await _client.GetAsync("/api/v1/public/transfers/d7w5f1q4/files/" + fileId.ToString("n") + "/download-url");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // still within cap — re-downloads allowed (T-003-06 step 2)
        }

        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(2, transfer.DownloadsCount); // both mints counted (1 → 2) — the cap guard allows this far
        Assert.Equal(2, await freshDb.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId)); // one row per mint
    }

    // ---- Exit check 3: cap reached → Status=3 flip + MAX_DOWNLOADS_REACHED (410); further mints keep failing idempotently ----

    [Fact]
    public async Task CapReached_StatusFlipsTo3_IdempotentMaxDownloadsReached()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d9w7f2q5", maxDownloads: 2);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "capped.txt", 10L)]);

        // Two mints succeed (0 → 2).
        for (var i = 0; i < 2; i++)
            using (var ok = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/files/" + fileId.ToString("n") + "/download-url"))
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Third mint: at cap → 410 MAX_DOWNLOADS_REACHED.
        using var atCap = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.Gone, atCap.StatusCode); // 410 — the closed table's status for MAX_DOWNLOADS_REACHED

        using (var doc = JsonDocument.Parse(await atCap.Content.ReadAsByteArrayAsync()))
        {
            Assert.Equal(ProblemCodes.MaxDownloadsReached, doc.RootElement.GetProperty("code").GetString()); // TA-4.2#6's canonical code
            Assert.Equal(410, doc.RootElement.GetProperty("status").GetInt32());
        }

        // Status flipped to 3 (the idempotent flip — US-005-03's literal; the flip lives in the store, not a worker).
        using var freshDb = FreshDb();
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(3, transfer.Status);

        // Fourth mint: still 410 (idempotent — the flip is idempotent by construction).
        using var atCapAgain = await _client.GetAsync("/api/v1/public/transfers/d9w7f2q5/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.Gone, atCapAgain.StatusCode);
        Assert.Equal(ProblemCodes.MaxDownloadsReached, JsonDocument.Parse(await atCapAgain.Content.ReadAsByteArrayAsync()).RootElement.GetProperty("code").GetString());

        // No extra rows: the rejected mints recorded nothing (the flip is in the DB; the mint was never attempted).
        var transferAfter = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(2, transferAfter.DownloadsCount); // still 2 — the failed mints did not increment
        Assert.Equal(2, await freshDb.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId)); // exactly the two successful rows
    }

    // ---- FR-003-9 style: 410 body byte-identical to endpoint 4's download-limit body for the same transfer ----

    [Fact]
    public async Task CapReached_410_BodyByteIdenticalToEndpoint4()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d3w9f4q6", maxDownloads: 1);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "identical.txt", 10L)]);

        // One mint succeeds (0 → 1). Next mint is at cap.
        using var first = await _client.GetAsync("/api/v1/public/transfers/d3w9f4q6/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Endpoint 4 and endpoint 6 both return the same Problem+JSON body (FR-003-9: single-body invariant).
        // D8 (endpoint 4's ExpiredVsUnknown precedent): pin one W3C trace id on both requests — correlationId is the
        // first 8 hex of the trace id, and FR-003-9 is byte identity, so both must carry it.
        const string traceparent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbccccddeeff01-01";
        using var ep6Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/d3w9f4q6/files/" + fileId.ToString("n") + "/download-url");
        ep6Req.Headers.Add("traceparent", traceparent);
        using var ep6 = await _client.SendAsync(ep6Req);
        Assert.Equal(HttpStatusCode.Gone, ep6.StatusCode); // 410 — the closed table's status for MAX_DOWNLOADS_REACHED

        using var ep4Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/d3w9f4q6");
        ep4Req.Headers.Add("traceparent", traceparent);
        using var ep4 = await _client.SendAsync(ep4Req);
        Assert.Equal(HttpStatusCode.Gone, ep4.StatusCode); // endpoint 4 also 410 on the same transfer (status already flipped)

        // Byte-identical: the two bodies must be exactly the same bytes (TA-4.2#6's FR-003-9).
        var ep6Body = await ep6.Content.ReadAsByteArrayAsync();
        var ep4Body = await ep4.Content.ReadAsByteArrayAsync();
        Assert.Equal(ep4Body, ep6Body); // FR-003-9 single-body invariant — endpoint 6's cap body == endpoint 4's
    }

    // ---- Unknown fileId under an active transfer → 404 + byte-identical body to endpoint 4's unknown linkId ----

    [Fact]
    public async Task UnknownFileId_404_BodyByteIdenticalToEndpoint4UnknownLink()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "d5w1f9q7", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "known.txt", 10L)]);

        // A well-formed but unknown fileId under an active transfer → 404.
        var unknownFileId = Guid.NewGuid().ToString("n");
        // D8 (same precedent as endpoint 4's ExpiredVsUnknown test): pin the same W3C trace id on both requests so
        // correlationId (first 8 hex of the trace id) matches — FR-003-9 is byte identity, not semantic equality.
        const string traceparent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbccccddeeff01-01";
        using var ep6Req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/transfers/d5w1f9q7/files/{unknownFileId}/download-url");
        ep6Req.Headers.Add("traceparent", traceparent);
        using var ep6 = await _client.SendAsync(ep6Req);
        Assert.Equal(HttpStatusCode.NotFound, ep6.StatusCode); // 404 — not 403, not 409, not 410

        using var ep4Req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/zzz99999"); // the unknown linkId case
        ep4Req.Headers.Add("traceparent", traceparent);
        using var ep4 = await _client.SendAsync(ep4Req);
        Assert.Equal(HttpStatusCode.NotFound, ep4.StatusCode);

        // FR-003-9: the bodies are byte-identical (same code, same details, no sender email leak).
        var ep6Body = await ep6.Content.ReadAsByteArrayAsync();
        var ep4Body = await ep4.Content.ReadAsByteArrayAsync();
        Assert.Equal(ep4Body, ep6Body); // FR-003-9 single-body invariant — no enumeration via fileIds (TA-9.6)
    }

    // ---- EC-003-3: Unicode filename round-trips exactly through the rs-cd override ----

    [Fact]
    public async Task UnicodeFilename_ContentDisposition_RoundTripsExactly()
    {
        const string fileName = "Q4 report (final) v2.pdf";
        var transferId = await SeedTransferAsync(status: 1, linkId: "d7w3f5q8", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, fileName, 10L)]);

        // Upload a small payload at the canonical path.
        await UploadBlobAsync(transferId, fileId, [1, 2, 3]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d7w3f5q8/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var url = new Uri(doc.RootElement.GetProperty("url").GetString()!);

        // GET the SAS directly (Azurite honors rs-cd) — StreamUrl re-addresses for IP-hosted Azurite + plain HTTP (T-055-05 shim).
        using var sas = new HttpClient();
        using var streamResponse = await sas.GetAsync(StreamUrl(url));
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);

        // The Content-Disposition header must round-trip exactly (EC-003-3: original filename preserved).
        // EC-003-3 round-trip proof: parse the RAW Content-Disposition header and require the decoded RFC 5987
        // filename to equal the original exactly. (HttpClient renders the disposition as "attachment; filename*=UTF-8''..." —
        // the HTTP-header form of the rs-cd override — so we assert on the raw values, not a stringified form.)
        // Content-Disposition is a CONTENT header in .NET's HttpClient model � it lives on the content headers, not the response.
        Assert.True(streamResponse.Content.Headers.TryGetValues("Content-Disposition", out var values), "Content-Disposition header present (content-level)");
        var raw = string.Join(",", values);
        Assert.StartsWith("attachment;", raw.TrimStart(), StringComparison.OrdinalIgnoreCase);
        const string marker = "filename*=UTF-8''";
        var encoded = raw.Substring(raw.IndexOf(marker) + marker.Length); // the percent-encoded original filename (nothing follows it in this header)
        Assert.Equal(fileName, Uri.UnescapeDataString(encoded)); // decoded == original — exact round-trip (EC-003-3)
    }

    // ---- EC-003-1: Range requests work natively against the SAS (full-size resume is T-003-09's Playwright scope) ----

    [Fact]
    public async Task RangeRequest_NativeAgainstSas_ReturnsPartialContent()
    {
        // ~100 KB payload — the full-size Range-resume test is T-003-09 (Playwright). This proves the mechanism.
        var fileSize = 102_400; // 100 KB
        var transferId = await SeedTransferAsync(status: 1, linkId: "d9w5f6q9", maxDownloads: 5);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "range.txt", (long)fileSize)]);

        // Upload 100 KB of random bytes.
        var bytes = new byte[fileSize];
        RandomNumberGenerator.Fill(bytes);
        await UploadBlobAsync(transferId, fileId, bytes);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d9w5f6q9/files/" + fileId.ToString("n") + "/download-url");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var url = new Uri(doc.RootElement.GetProperty("url").GetString()!);

        // Range: bytes=0-99 → 206 Partial Content with the first 100 bytes (StreamUrl re-addresses for Azurite + plain HTTP).
        using var sas = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, StreamUrl(url));
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-99");
        using var partialResponse = await sas.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partialResponse.StatusCode); // 206 — native Azurite Range support

        var partialBytes = await partialResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(100, partialBytes.Length); // exactly the requested range length
        // The first 100 bytes must match the uploaded payload (EC-003-1: direct streaming, no proxying).
        Assert.Equal(bytes[..100], partialBytes);
    }

    // ---- Gated transfer: mint without ?t= → 401 UNAUTHENTICATED; POST /unlock then mint with ?t= → 200 ----

    [Fact]
    public async Task GatedTransfer_MintWithoutToken_Returns401()
    {
        var password = "secret-pass-123";
        var transferId = await SeedTransferAsync(status: 1, linkId: "d1w7f8q0", maxDownloads: 5, password: password);
        var fileId = Guid.NewGuid();
        await SeedFileItemsAsync(transferId, [(fileId, "gated.txt", 10L)]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/d1w7f8q0/files/" + fileId.ToString("n") + "/download-url");
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

        // POST /unlock to get a token (T-003-04's pattern).
        using var unlockResponse = await _client.PostAsync(
            "/api/v1/public/transfers/d3w9f8q2/unlock",
            new StringContent(JsonSerializer.Serialize(new { password }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        using var unlockDoc = JsonDocument.Parse(await unlockResponse.Content.ReadAsByteArrayAsync());
        var token = unlockDoc.RootElement.GetProperty("token").GetString()!;

        // Mint with the valid ?t= token → 200.
        using var mintResponse = await _client.GetAsync(
            "/api/v1/public/transfers/d3w9f8q2/files/" + fileId.ToString("n") + "/download-url?t=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.OK, mintResponse.StatusCode); // 200 — the token clears the gate (TA-9.6#1)

        using var doc = JsonDocument.Parse(await mintResponse.Content.ReadAsByteArrayAsync());
        // The SAS URL is returned (the Ok state).
        Assert.True(doc.RootElement.TryGetProperty("url", out _));
    }

    // ---- Helpers (same seed pattern as PublicTransferEndpointTests/UnlockEndpointTests) ----

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

    /// <summary>Upload blob bytes to the canonical transfer-file path (TA-3.5) — endpoint 6's SAS streams from here.</summary>
    private async Task UploadBlobAsync(Guid transferId, Guid fileId, byte[] bytes)
    {
        // BlobPaths.TransferFile returns the canonical TA-3.5 path "transfers/{t}/files/{f}" where
        // the FIRST segment is the container. SplitPath gives us (container="transfers", blobName="{t}/files/{f}").
        var path = BlobPaths.TransferFile(transferId, fileId);
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

    // ---- T-055-05's transport-local helpers, copied for this fixture: re-address the minted single-file read SAS for an IP-hosted
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
        var rscdPart = parts.FirstOrDefault(p => p.k == "rscd");
        var rscdVal = rscdPart.k.Length > 0 ? Uri.UnescapeDataString(rscdPart.v) : string.Empty;

        // TA-3.6 16-line blob SAS string-to-sign: sp=r (read), no start bound, Azurite canonical path,
        // protocol re-signed for the plain-HTTP leg, rs-cd line = the original-filename override when present.
        const string ProtocolLine = "https,http";
        var stringToSign = string.Join('\n', new[]
        {
            "r",                                     // sp — read (TA-3.6 recipient file)
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
            rscdVal,                                // content disposition — the rs-cd override (EC-003-3) when present
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
