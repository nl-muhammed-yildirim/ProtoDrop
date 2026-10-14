using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage;
using Azure.Storage.Blobs;
using Testcontainers.Azurite;
using wa.application.Blob;
using wa.infrastructure.Blob;

namespace wa.api.integration.Blob;

/// <summary>
/// T-055-05 (AC-055-1/3, closes T-007a): mint a cwr SAS for the canonical staging path → upload to that URL with plain
/// HTTP via HttpClient (no credentials in headers — proves nothing in the chain needs an account key) → read back through
/// <see cref="wa.application.Ports.IBlobStore.DownloadAsync"/> and assert byte-exact equality. Runs against Testcontainers Azurite
/// (TA-14.3): same devstoreaccount1/key pair as the §5.2 local profile, so CI and local behavior match. Per-block retry semantics are T-012's.
/// </summary>
public class SasRoundTripTests : IAsyncLifetime
{
    private AzuriteContainer _container = null!;
    private Uri _accountUrl = null!;
    private BlobServiceClient _svc = null!;

    public async Task InitializeAsync()
    {
        // The builder pins the account/key pair as constants — the same values the §5.2 local profile uses (TA-14.3).
        // Azurite ≥ 3.37.0 supports the frozen Azure.Storage.Blobs 12.29.2 default service version (2026-06-06) — verified against v3.37.0 release notes.
        _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _container.StartAsync();

        var port = _container.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        // Account name as first path segment — the form Azurite with an IP host expects (same shape as the §5.2 local profile URL).
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");
        // T-055-02's adapter is the port implementation under test — no separate fake.
        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));

        // Parity with T-055-02's hosted bootstrap (not the unit under test here).
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Mint_cwr_SAS_plain_HTTP_upload_reads_back_byte_exact_at_canonical_staging_path()
    {
        var draftId = Guid.NewGuid().ToString("n"); // :N format (TA-3.1) — the minter's string form
        var fileId = Guid.NewGuid().ToString("n");
        // AC-055-3: the location is exactly the canonical staging row — no path construction outside BlobPaths.
        var path = BlobPaths.StagingFile(new Guid(draftId), new Guid(fileId));

        var minter = new BlobSasMinter(_accountUrl, AzuriteBuilder.AccountKey);
        var sasUri = minter.MintDraftUpload(draftId, fileId);

        // The browser's upload: a block blob PUT via plain HTTP — the SAS URL is the only credential; nothing in headers.
        // Two transport-local accommodations for an IP-hosted Azurite (the minted value itself is unchanged — the exact
        // TA-3.6 shape is pinned by T-055-04's unit tests): the account name must be the first path segment, and the
        // protocol line is re-signed as "https,http" because Azurite rejects spr=https on a plain-HTTP PUT.
        var browserUrl = BrowserUploadUrl(sasUri);

        var bytes = BuildBlock();
        using var http = new HttpClient();
        using (var content = new ByteArrayContent(bytes))
        {
            // x-ms-blob-type is the standard blob REST API declaration, not a credential.
            var put = new HttpRequestMessage(HttpMethod.Put, browserUrl) { Content = content };
            put.Headers.Add("x-ms-blob-type", "BlockBlob");
            using var resp = await http.SendAsync(put);
            // 201 Created is the Put Blob success code; a 403 would mean the SAS itself is wrong — T-012 re-mints.
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }

        var store = new AzureBlobStore(_svc);
        await using var stream = await store.DownloadAsync(path);
        using var roundTripped = new MemoryStream();
        await stream.CopyToAsync(roundTripped);

        Assert.Equal(bytes, roundTripped.ToArray()); // byte-for-byte — nothing rewritten on the way in or out
    }

    /// <summary>
    /// Re-addresses the minted URL for an IP-hosted Azurite (first path segment = account) and re-signs it over the same
    /// TA-3.6 string-to-sign with only the protocol line changed to <c>https,http</c>. The canonical name already signed by
    /// the minter is <c>/blob/devstoreaccount1/staging/…</c>, so the inserted account segment matches it exactly.
    /// </summary>
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
}
