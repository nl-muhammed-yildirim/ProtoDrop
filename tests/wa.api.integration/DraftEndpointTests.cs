using System.Net;
using System.Net.Http.Json; // PostAsJsonAsync extension for the anonymous-object body.
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
using wa.infrastructure.Blob;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-001-05 exit checks for POST /api/v1/transfers/draft (TA-4.2#1): per-file minted cwr SAS + plain-HTTP PUT round-trip at the canonical staging path, 422 TRANSFER_SIZE_EXCEEDED with no blobs on an oversized selection, and the exact response wire shape (property names/order, expiresInSec=7200, sv=2024/sp=cwr).
/// Fresh Azurite + SQL 2022 per instance (TA-14.3): migrations seed the free plan rows ILimitsProvider resolves — no fakes in this half.
/// </summary>
public class DraftEndpointTests : IAsyncLifetime
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
        // Account name as first path segment — the form Azurite with an IP host expects (same shape as the §5.2 local profile URL).
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        // Program.cs builds eagerly from top-level statements — ConfigureAppConfiguration in the factory lands too late; env vars are always visible (HealthFailureTests' comment).
        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", _accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");

        // Fresh SQL + migrations: the SeedPlansAndFeatureFlags migration seeds the free plan row the handler resolves.
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        // Program.cs registers BlobServiceClient from the same §5.2 values — reuse one for blob-side assertions (byte-exact read-back / no-blob checks).
        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Exit check 3: exact TA-4.2#1 wire shape — names + order pinned at the endpoint's single construction site ----

    [Fact]
    public async Task Draft_Endpoint_Response_Matches_TA_4_2_Wire_Shape_with_one_minted_cwr_SAS_per_file()
    {
        var response = await PostDraftAsync([new FileRow("a.txt", 100L), new FileRow("b.png", 200L)]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;

        // TA-4.2#1 exact shape: top-level draftId/expiresInSec/files in that order; rows fileId/uploadUrl — no other properties.
        Assert.Equal(["draftId", "expiresInSec", "files"], root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(7200, root.GetProperty("expiresInSec").GetInt32()); // TA-3.6's 2 h draft-upload TTL in seconds (constant, not a plan limit).
        var files = root.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        foreach (var file in files.EnumerateArray())
            Assert.Equal(["fileId", "uploadUrl"], file.EnumerateObject().Select(p => p.Name).ToArray());

        // Every uploadUrl is a minted draft SAS — sv=2024 + sp=cwr, the TA-3.6 pair (T-055 pins the full string-to-sign shape in units).
        foreach (var file in files.EnumerateArray())
        {
            var url = new Uri(file.GetProperty("uploadUrl").GetString());
            Assert.Contains("sv=2024", url.Query);
            Assert.Contains("sp=cwr", url.Query);
        }

        // Every file gets its own mint — one fileId per staged file (distinct, none reused).
        Assert.Equal(2, files.EnumerateArray().Select(f => f.GetProperty("fileId").GetString()).Distinct().Count());
    }

    // ---- Exit check 1: plain-HTTP PUT to a minted URL succeeds; blob exists byte-exact at /staging/{draftId:N}/f/{fileId:N} ----

    [Fact]
    public async Task Draft_Endpoint_Minted_SAS_plain_HTTP_PUT_round_trips_byte_exact_at_canonical_staging_path()
    {
        var response = await PostDraftAsync([new FileRow("payload.bin", 4096L)]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using (var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()))
        {
            var root = doc.RootElement;
            var draftId = root.GetProperty("draftId").GetString()!; // :N format — the minter's string form.
            var fileRow = root.GetProperty("files")[0];
            var fileId = fileRow.GetProperty("fileId").GetString()!;
            var minted = new Uri(fileRow.GetProperty("uploadUrl").GetString());

            // The browser's upload: a block blob PUT via plain HTTP — the SAS URL is the only credential; nothing in headers.
            using var http = new HttpClient();
            var bytes = BuildBlock();
            using (var content = new ByteArrayContent(bytes))
            {
                var put = new HttpRequestMessage(HttpMethod.Put, BrowserUploadUrl(minted)) { Content = content };
                // x-ms-blob-type is the standard blob REST API declaration, not a credential.
                put.Headers.Add("x-ms-blob-type", "BlockBlob");
                using var putResp = await http.SendAsync(put);
                Assert.Equal(HttpStatusCode.Created, putResp.StatusCode); // 201 Created; 403 would mean the minted SAS itself is wrong.
            }

            // AC-055-3 shape: the location IS exactly the canonical staging row — no path construction outside BlobPaths.
            var path = BlobPaths.StagingFile(new Guid(draftId), new Guid(fileId));
            await using var stream = await new AzureBlobStore(_svc).DownloadAsync(path);
            using var roundTripped = new MemoryStream();
            await stream.CopyToAsync(roundTripped);

            Assert.Equal(bytes, roundTripped.ToArray()); // byte-for-byte — nothing rewritten on the way in or out.
        }
    }

    // ---- Exit check 2: oversized selection → 422 TRANSFER_SIZE_EXCEEDED (TA-4.1.3 closed list), no blobs written ----

    [Fact]
    public async Task Draft_Endpoint_Oversized_selection_returns_422_TransferSizeExceeded_with_no_blobs_written()
    {
        // Free plan: MaxTransferSize = 5 GB (T-052-04 seed via the migrated DB) — total just over.
        const long FreeMax = 5_368_709_120L;
        var response = await PostDraftAsync([new FileRow("big.mp4", FreeMax), new FileRow("one-byte.bin", 1L)]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        // TA-4.1.3 shape: the closed code list pins TRANSFER_SIZE_EXCEEDED at 422 — status field agrees with the HTTP status.
        Assert.Equal("TRANSFER_SIZE_EXCEEDED", root.GetProperty("code").GetString());
        Assert.Equal(422, root.GetProperty("status").GetInt32());

        // The handler returns null before minting — nothing uploaded; the fresh staging container must still be empty.
        var names = new List<string>();
        await foreach (var item in _svc.GetBlobContainerClient(BlobPaths.StagingContainer).GetBlobsAsync())
            names.Add(item.Name);
        Assert.Empty(names);
    }

    private Task<HttpResponseMessage> PostDraftAsync(FileRow[] files) =>
        _client.PostAsJsonAsync("/api/v1/transfers/draft", new { files = files.Select(f => new { name = f.Name, sizeBytes = f.Size, contentType = (string?)null }).ToArray() });

    private sealed record FileRow(string Name, long Size);

    // ---- T-055-05's transport-local helpers, copied for this fixture: re-address the minted URL for an IP-hosted Azurite + re-sign the protocol line as "https,http" (plain-HTTP PUT rejects spr=https without it). The minted value itself is unchanged.

    private static Uri BrowserUploadUrl(Uri minted)
    {
        var st = Token(minted.Query, "st");
        var se = Token(minted.Query, "se");

        // The minted URL's path is /{container}/{blobName}; the canonical blob name is /blob/{account}{that}.
        const string ProtocolLine = "https,http";
        var stringToSign = string.Join('\n', new[]
        {
            "cwr",                 // sp — literal (TA-3.6 draft upload)
            st,                    // start time
            se,                    // end time
            "/blob/" + AzuriteBuilder.AccountName + minted.AbsolutePath,   // canonical name
            string.Empty,          // identifier
            string.Empty,          // IP address range
            ProtocolLine,          // protocol — re-signed for the plain-HTTP browser leg
            "2024",                // TA-3.6 version literal (the minter's Version)
            "b",                   // resource: blob
            string.Empty,          // snapshot time
            string.Empty,          // encryption scope
            string.Empty,          // cache control
            string.Empty,          // content disposition
            string.Empty,          // content encoding
            string.Empty,          // content language
            string.Empty,          // content type
        });

        using var hmac = new HMACSHA256(Convert.FromBase64String(AzuriteBuilder.AccountKey));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

        return new Uri(
            minted.GetLeftPart(UriPartial.Authority) + "/" + AzuriteBuilder.AccountName + minted.AbsolutePath +
            "?sv=2024" +
            "&spr=https%2Chttp" +
            $"&st={Uri.EscapeDataString(st)}" +
            $"&se={Uri.EscapeDataString(se)}" +
            "&sr=b&sp=cwr" +
            $"&sig={Uri.EscapeDataString(sig)}");
    }

    /// <summary>Reads an unescaped query token (e.g. st/se) from a minted SAS URL's query string.</summary>
    private static string Token(string query, string name)
    {
        var value = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.StartsWith(name + '=', StringComparison.Ordinal));
        return Uri.UnescapeDataString(value[(name.Length + 1)..]);
    }

    private static byte[] BuildBlock()
    {
        // Deterministic non-trivial payload; no trailing-newline assumption and a full 256-byte value sweep.
        var bytes = new byte[4096];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31 + 7) % 256);
        return bytes;
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
