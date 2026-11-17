using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-014 exit checks for GET /api/v1/public/transfers/{linkId} (TA-4.2#4):
/// active full metadata payload (no SAS minted), terminal-state Problem+JSON, expired-vs-unknown byte identity (FR-003-9).
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as SendEndpointTests. Rows are seeded directly via WaDbContext
/// (D7: FinalizeTransfer hardcodes MaxDownloads=1, so direct seeding controls the columns precisely).
/// </summary>
public class PublicTransferEndpointTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        // Endpoint 4 is DB-only (no SAS minted at page view) — Azurite is still required by Program.cs's blob wiring (TA-3.5).
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

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Exit check 1: Active → full metadata payload; no SAS minted (zero DownloadEvent rows, DownloadsCount unchanged) ----

    [Fact]
    public async Task Active_ReturnsFullMetadata_NoSasMinted()
    {
        var transferId = await SeedTransferAsync(
            status: 1, linkId: "a1b2c3d4", senderEmail: null, note: "Here's your file",
            maxDownloads: 5, downloadsCount: 2, totalBytes: 1500);
        var files = new[]
        {
            (fileId: Guid.NewGuid(), name: "first.txt", sizeBytes: 1000L),
            (fileId: Guid.NewGuid(), name: "second.txt", sizeBytes: 500L)
        };
        await SeedFileItemsAsync(transferId, files);

        using var response = await _client.GetAsync("/api/v1/public/transfers/a1b2c3d4");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // TA-4.2#4 exact wire shape — property names pinned at the single construction site (endpoints 1–3 style).
        Assert.Equal(["from", "note", "files", "hasDownloadAll", "downloadsLeft", "passwordRequired"],
            root.EnumerateObject().Select(p => p.Name).ToArray());

        Assert.Equal("Alice", root.GetProperty("from").GetString());
        Assert.Equal("Here's your file", root.GetProperty("note").GetString());

        // File rows in SortOrder ascending; fileId in the same "n" format as endpoints 1–3.
        var filesEl = root.GetProperty("files");
        Assert.Equal(2, filesEl.GetArrayLength());
        var firstRow = filesEl[0];
        Assert.Equal(["fileId", "name", "sizeBytes"], firstRow.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(files[0].fileId.ToString("n"), firstRow.GetProperty("fileId").GetString());
        Assert.Equal("first.txt", firstRow.GetProperty("name").GetString());
        Assert.Equal(1000, firstRow.GetProperty("sizeBytes").GetInt64());
        var secondRow = filesEl[1];
        Assert.Equal(files[1].fileId.ToString("n"), secondRow.GetProperty("fileId").GetString());
        Assert.Equal("second.txt", secondRow.GetProperty("name").GetString());
        Assert.Equal(500, secondRow.GetProperty("sizeBytes").GetInt64());

        Assert.True(root.GetProperty("hasDownloadAll").GetBoolean()); // ≥2 files AND 1500 bytes ≤ the seeded free-plan MaxZipSize (TA-3.4: via ILimitsProvider)
        Assert.Equal(3, root.GetProperty("downloadsLeft").GetInt32()); // 5 - 2 — same column TA-7.2's flip uses
        Assert.False(root.GetProperty("passwordRequired").GetBoolean());

        // No SAS minted: zero DownloadEvent rows for this transfer and DownloadsCount unchanged (the endpoint never counts).
        Assert.Equal(0, await _db.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId));
        var dbTransfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(2, dbTransfer.DownloadsCount);
    }

    // ---- Exit check 3: downloadsLeft correct for finite caps; null when the cap is ∞ ----

    [Fact]
    public async Task Active_FiniteCap_ReturnsCorrectDownloadsLeft()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "f1n1t3c1", maxDownloads: 5, downloadsCount: 2);

        using var response = await _client.GetAsync("/api/v1/public/transfers/f1n1t3c1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(3, doc.RootElement.GetProperty("downloadsLeft").GetInt32()); // 5 - 2
    }

    [Fact]
    public async Task Active_UnlimitedCap_ReturnsNullDownloadsLeft()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "k5w1q1t3", maxDownloads: -1, downloadsCount: 0); // MaxDownloads=-1 → Business (∞)

        using var response = await _client.GetAsync("/api/v1/public/transfers/k5w1q1t3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("downloadsLeft").ValueKind); // null on the wire — FR-003-5 hides the line for ∞ caps
    }

    // ---- AC-003-3: Expired → 410 + Problem+JSON, no file list, no SAS minted ----

    [Fact]
    public async Task Expired_410_TransferExpired_ProblemShape_NoSasMinted()
    {
        var transferId = await SeedTransferAsync(status: 2, linkId: "3xpwr3d1", senderEmail: "alice@example.com");
        await SeedFileItemsAsync(transferId, [(Guid.NewGuid(), "secret.txt", 12345L)]); // a FileItem exists — the terminal body must not leak it

        using var response = await _client.GetAsync("/api/v1/public/transfers/3xpwr3d1");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode); // 410

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // TA-4.1.3 exact field set — type, title, status, code, details, correlationId (no errors[] here).
        Assert.Equal(["type", "title", "status", "code", "details", "correlationId"],
            root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("https://example.com/errors/transfer_expired", root.GetProperty("type").GetString());
        Assert.Equal("Transfer expired", root.GetProperty("title").GetString());
        Assert.Equal(410, root.GetProperty("status").GetInt32());
        Assert.Equal("TRANSFER_EXPIRED", root.GetProperty("code").GetString()); // the closed list — no new code (D2)
        Assert.Equal("This transfer has expired. Contact alice@example.com for a new link.", root.GetProperty("details").GetString());

        // AC-003-3: no file list, no SAS minted.
        Assert.False(doc.RootElement.TryGetProperty("files", out _));
        Assert.Equal(0, await _db.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId));
    }

    // ---- AC-003-4 / FR-003-9: Unknown linkId → 404 + the same Problem+JSON code as expired (same body, different status line only) ----

    [Fact]
    public async Task UnknownLinkId_404_SameProblemCode()
    {
        using var response = await _client.GetAsync("/api/v1/public/transfers/zzz99999"); // valid charset (D5), no row

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404 — never 410, never 400 for malformed input

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("TRANSFER_EXPIRED", root.GetProperty("code").GetString()); // indistinguishable from expired except the status (FR-003-9)
        Assert.Equal(404, root.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task ExpiredVsUnknown_BodiesByteIdenticalExceptStatus()
    {
        // D8: pin the same W3C trace id on both requests so correlationId (first 8 hex of the trace id) matches.
        const string traceparent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbccccddeeff01-01";

        var transferId = await SeedTransferAsync(status: 2, linkId: "3xpwr3d1", senderEmail: null); // no sender email — details must equal the unknown case
        await SeedFileItemsAsync(transferId, [(Guid.NewGuid(), "secret.txt", 12345L)]);

        using var expiredReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/3xpwr3d1");
        expiredReq.Headers.Add("traceparent", traceparent);
        using var expiredRes = await _client.SendAsync(expiredReq);
        Assert.Equal(HttpStatusCode.Gone, expiredRes.StatusCode);

        using var unknownReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/transfers/zzz99999");
        unknownReq.Headers.Add("traceparent", traceparent);
        using var unknownRes = await _client.SendAsync(unknownReq);
        Assert.Equal(HttpStatusCode.NotFound, unknownRes.StatusCode);

        var expiredBody = await expiredRes.Content.ReadAsByteArrayAsync();   // 410 — seeded Status=2, SenderEmail=null
        var unknownBody = await unknownRes.Content.ReadAsByteArrayAsync();   // 404 — non-existent linkId

        // Both bodies parse as Problem+JSON with code TRANSFER_EXPIRED.
        Assert.Equal("TRANSFER_EXPIRED", JsonDocument.Parse(expiredBody).RootElement.GetProperty("code").GetString());
        Assert.Equal("TRANSFER_EXPIRED", JsonDocument.Parse(unknownBody).RootElement.GetProperty("code").GetString());

        // Byte-identical except status (410 vs 404): FR-003-9. The status value is the only body difference — everything else
        // (type, title, code, details, correlationId) must match byte-for-byte. D8: replace "status":410 with "status":404 and require exact equality.
        Assert.Equal(expiredBody.Length, unknownBody.Length); // identical byte lengths — the only difference is status (410 vs 404)
        var expected = System.Text.Encoding.UTF8.GetString(expiredBody).Replace("\"status\":410", "\"status\":404"); // single occurrence — the status field (D8)
        Assert.Equal(expected, System.Text.Encoding.UTF8.GetString(unknownBody)); // FR-003-9: same body, different status line only
    }

    // ---- AC-003-5: DownloadLimit → 410 + Problem+JSON code MAX_DOWNLOADS_REACHED (409→410 explicit override, D2) ----

    [Fact]
    public async Task DownloadLimit_410_MaxDownloadsReached()
    {
        var transferId = await SeedTransferAsync(status: 3, linkId: "q1w1t3d3", maxDownloads: 1, downloadsCount: 1);

        using var response = await _client.GetAsync("/api/v1/public/transfers/q1w1t3d3");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode); // 410 — the task doc's status mapping for Status=3

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("MAX_DOWNLOADS_REACHED", root.GetProperty("code").GetString()); // the closed list — no new code (D2)
        Assert.Equal(410, root.GetProperty("status").GetInt32());
        Assert.Equal("All downloads have been used.", root.GetProperty("details").GetString()); // AC-003-5 alternative flow: SenderEmail null → plain message, no fabricated address

        // AC-003-5: no file list.
        Assert.False(doc.RootElement.TryGetProperty("files", out _));

        Assert.Equal(0, await _db.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId));
    }

    // ---- AC-003-5 / F-TRF-005-4: download-limit details include the sender email when provided ----

    [Fact]
    public async Task DownloadLimit_410_SenderEmailIncludedInDetails_WhenProvided()
    {
        var transferId = await SeedTransferAsync(status: 3, linkId: "q2w2t2d2", maxDownloads: 1, downloadsCount: 1, senderEmail: "alice@example.com");

        using var response = await _client.GetAsync("/api/v1/public/transfers/q2w2t2d2");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode); // 410

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("MAX_DOWNLOADS_REACHED", root.GetProperty("code").GetString());
        Assert.Equal("All downloads have been used. Contact alice@example.com for a new link.", root.GetProperty("details").GetString()); // F-TRF-005-4: sender email when provided

        // AC-003-5: no file list.
        Assert.False(doc.RootElement.TryGetProperty("files", out _));

        Assert.Equal(0, await _db.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId));
    }

    // ---- FR-003-2 / plan §3: files returned ordered by SortOrder — rows seeded out-of-order (inserted reversed) ----

    [Fact]
    public async Task Active_FilesReturnedSortedBySortOrder_WhenSeededOutOfOrder()
    {
        var transferId = await SeedTransferAsync(status: 1, linkId: "s0r4q3d5", maxDownloads: 5, downloadsCount: 2, totalBytes: 0);

        // Insertion order (i) deliberately contradicts SortOrder: the first inserted row has SortOrder=2, etc.
        var firstInserted = (fileId: Guid.NewGuid(), name: "third-by-sort.txt", sizeBytes: 300L, sortOrder: 2);
        var secondInserted = (fileId: Guid.NewGuid(), name: "first-by-sort.txt", sizeBytes: 100L, sortOrder: 0);
        var thirdInserted = (fileId: Guid.NewGuid(), name: "second-by-sort.txt", sizeBytes: 200L, sortOrder: 1);
        await SeedFileItemsAsync(transferId, [firstInserted, secondInserted, thirdInserted]);

        using var response = await _client.GetAsync("/api/v1/public/transfers/s0r4q3d5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        // GET returns files sorted by SortOrder ascending through the real adapter — not insertion order.
        Assert.Equal(["first-by-sort.txt", "second-by-sort.txt", "third-by-sort.txt"],
            doc.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray());
    }

    // ---- F-TRF-003 test plan: Deleted → 404 + same shape as unknown (DECISION A: Status=4 maps to the unknown branch) ----

    [Fact]
    public async Task DeletedStatus_404_SameShapeAsUnknown()
    {
        var transferId = await SeedTransferAsync(status: 4, linkId: "d3q3t3d1", senderEmail: null);
        await SeedFileItemsAsync(transferId, [(Guid.NewGuid(), "gone.txt", 100L)]); // FileItems present — the 404 body must not leak them (byte-leak audit)

        using var response = await _client.GetAsync("/api/v1/public/transfers/d3q3t3d1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // 404 — indistinguishable from unknown (DECISION A)

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("TRANSFER_EXPIRED", root.GetProperty("code").GetString()); // same code as expired/unknown
        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("files", out _));
    }

    // ---- Helpers (seeded directly via WaDbContext — D7: rows control MaxDownloads/DownloadsCount precisely) ----

    private async Task<Guid> SeedTransferAsync(int status, string linkId, string? senderEmail = null, int maxDownloads = 1, int downloadsCount = 0, string? passwordHash = null, long totalBytes = 0, string? note = null)
    {
        var id = Guid.NewGuid();
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
            Note = note,
            PasswordHash = passwordHash,
            TotalBytes = totalBytes,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedFileItemsAsync(Guid transferId, (Guid fileId, string name, long sizeBytes)[] files)
        => await SeedFileItemsAsync(transferId, files.Select(f => (f.fileId, f.name, f.sizeBytes, sortOrder: -1)).ToArray());

    private async Task SeedFileItemsAsync(Guid transferId, (Guid fileId, string name, long sizeBytes, int sortOrder)[] files)
    {
        // UQ_FileItem_T_B: (TransferId, BlobRefId) is unique — one BlobRef per FileItem (same style as FinalizeTransfer).
        for (var i = 0; i < files.Length; i++)
        {
            var (fileId, name, sizeBytes, sortOrder) = files[i];
            var blobRef = new BlobRef
            {
                Id = Guid.NewGuid(),
                BlobPath = $"transfers/{transferId:N}/{fileId:N}",
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
                OriginalName = name,
                SizeBytes = sizeBytes,
                ContentType = "text/plain",
                SortOrder = sortOrder >= 0 ? sortOrder : i // explicit when provided (out-of-order tests); distinct ascending otherwise (TA-4.2#4)
            });
        }
        // Keep Transfer columns consistent with the FileItem rows (same as finalize does in production):
        // hasDownloadAll reads Transfer.FileCount/TotalBytes, not the FileItem rows.
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = files.Length;
        transfer.TotalBytes = files.Sum(f => f.sizeBytes);
        await _db.SaveChangesAsync();
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
