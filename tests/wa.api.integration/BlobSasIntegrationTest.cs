using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using wa.domain;
using wa.infrastructure.Blob;

namespace wa.api.integration;

/// <summary>
/// T-007c / T-007 exit-check integration test: "mint <c>cwr</c> SAS →
/// upload a block → read it back". Exercises the whole draft-upload path
/// end-to-end against a disposable Azurite container
/// (<see cref="Testcontainers.Azurite.AzuriteBuilder"/>, TA-2.6):
///
///   1. mint a draft-upload SAS with production <see cref="BlobSasMinter"/>
///      and check its <c>sp=cwr</c> / <c>sr=b</c> / <c>sv=2024-08-04</c>
///      / <c>spr=https</c> shape is absolute and the TA-3.6 staging path;
///   2. simulate the browser's upload by PUTing a small block blob to the
///      minted URL over plain HTTP with a bare <see cref="HttpClient"/>
///      (no credentials) — the transport-local <c>spr=https,http</c>
///      re-signing is the accommodation (see below);
///   3. read the block back through <see cref="IBlobStore"/> and assert
///      the exact bytes round-trip.
///
/// Two transport-local accommodations (the production values are unchanged
/// — see <c>BlobSasMinterTest</c> for the exact TA-3.6 mint/TA-3.6
/// string-to-sign assertions):
///   * <c>Azurite</c> with an IP-address host treats the FIRST path segment
///     as the account, so the browser-leg URL inserts
///     <c>/devstoreaccount1</c> between host and <c>/staging/…</c> — which
///     also matches the canonical name the minter signed
///     (<c>/blob/devstoreaccount1/staging/…</c>), so only <c>sig</c> +
///     <c>spr</c> move.
///   * <c>Azurite</c> <c>validateProtocol</c> rejects <c>spr=https</c> on a
///     plain-HTTP PUT, so <c>spr</c> is re-signed as <c>https,http</c> (the
///     <c>sig</c> is recomputed over the same TA-3.6 string-to-sign with the
///     protocol line = <c>https,http</c>).
/// </summary>
public class BlobSasIntegrationTest
{
    // TA-3.5 staging path + fixed ids — GUID-shaped, lower-invariant.
    const string DraftId = "d1";
    const string FileId = "f1";
    const string Account =
        "devstoreaccount1"; // == AzuriteBuilder.AccountName
    // The SAS is scoped to a single BLOB; the name on-storage (and in the
    // canonical name) is the staging path MINUS the container prefix.
    const string BlobName = "d1/f/f1"; // == BlobPaths.Staging(...)[8..]
    const string Container = "staging"; // == BlobPaths.Staging(...)[..7]

    // Live clock — Azurite (a real-time emulator) validates <c>se</c>
    // against the host clock, so a fixed past clock (the unit test's
    // 2026-02-06) would 403 as expired; the <see cref="BlobSasMinterTest"/>
    // already pins the clock for exact <c>st</c>/<c>se</c>/<c>sig</c>
    // assertions, so here we only need a non-expired SAS.
    static readonly DateTimeOffset Clock =
        DateTimeOffset.UtcNow;

    static readonly byte[] Payload =
        new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

    [Fact]
    public async Task Mint_Cwr_Sas_BrowserUpload_UploadReadBackIsEqual()
    {
        await using var azurite = Builder().Build();
        await azurite.StartAsync();
        var endpoint = new Uri(azurite.GetBlobEndpoint());

        // Production store + minter pointed at the live Azurite account.
        var store = new AzureBlobStore(endpoint,
            AzuriteBuilder.AccountKey,
            NullLogger<AzureBlobStore>.Instance);
        await store.CreateContainerIfNotExistsAsync("staging");
        var minter = new BlobSasMinter(endpoint, AzuriteBuilder.AccountKey);

        // ── Arrange/Act: mint the draft-upload SAS ──────────────────
        var minted = minter.MintDraftUpload(DraftId, FileId, Clock);

        // (1) TA-3.6 shape: cwr permissions, blob scope, Version 2024,
        //     https protocol, and the TA-3.5 staging path on an
        //     absolute URL.
        var q = Query(minted);
        Assert.Equal("cwr", q["sp"]);
        Assert.Equal("b", q["sr"]);
        Assert.Equal("2024-08-04", q["sv"]);
        Assert.Equal("https", q["spr"]);
        Assert.True(minted.IsAbsoluteUri);
        Assert.Equal(
            endpoint.GetLeftPart(UriPartial.Authority) +
            "/" + BlobPaths.Staging(DraftId, FileId),
            minted.GetLeftPart(UriPartial.Path));

        // ── Browser leg: plain-HTTP PUT to the (re-addressed,
        //     re-signed) SAS URL — no credentials.
        var browserUrl = BrowserUploadUrl(minted, endpoint);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var content = new ByteArrayContent(Payload);
        content.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");
        using var put = new HttpRequestMessage(HttpMethod.Put,
            new Uri(browserUrl))
        { Content = content };
        put.Headers.Add("x-ms-blob-type", "BlockBlob");
        using var resp = await http.SendAsync(put);
        await using var respBody = new MemoryStream();
        await resp.Content.CopyToAsync(respBody);
        Assert.True((int)resp.StatusCode is >= 200 and < 300,
            $"browser PUT expected 2xx, got {(int)resp.StatusCode}; " +
            $"browserUrl={browserUrl}: " +
            Encoding.UTF8.GetString(respBody.ToArray()));

        // ── Read it back through <see cref="IBlobStore"/>.
        var roundTripped = await ReadAllAsync(
            store, Container, BlobName);
        Assert.Equal(Payload, roundTripped);
    }

    // ── helpers ───────────────────────────────────────────────────────

    // Testcontainers helper (the image is pinned by AGENT.md local env and
    // is a dev-only throwaway; the <c>cwr</c> upload + read-back proves the
    // SAS the browser will use, not the emulator's internals).
    static AzuriteBuilder Builder() =>
        new("mcr.microsoft.com/azure-storage/azurite:latest");

    // The browser-leg URL: the minted base is
    // <c>http://{host}:{port}/staging/{draft}/f/{file}</c>. <c>Azurite</c>
    // (IP host) reads the FIRST path segment as the account, so insert
    // <c>/{Account}</c> before <c>/staging/…</c> and re-sign as
    // <c>spr=https,http</c>.
    static string BrowserUploadUrl(Uri minted, Uri endpointAuthority)
    {
        var q = Query(minted);
        var st = q["st"]; // minted draft SAS always carries st.
        var se = q["se"];
        var canonical = $"/blob/{Account}/{Container}/{BlobName}";
        var sig = Sign("cwr", Unescape(st), Unescape(se), canonical,
            "https,http");
        // <c>spr</c> is a bare token (only the comma is escaped, <c>=</c> is
        // literal) so the server reads <c>spr</c> — the re-signed protocol
        // line matches what it will validate against.
        return
            $"{endpointAuthority.GetLeftPart(UriPartial.Authority)}/{Account}/" +
            BlobPaths.Staging(DraftId, FileId) +
            $"?sv={q["sv"]}&spr=https%2Chttp" +
            $"&st={Uri.EscapeDataString(Unescape(st))}" +
            $"&se={Uri.EscapeDataString(Unescape(se))}" +
            $"&sr={q["sr"]}&sp={q["sp"]}" +
            $"&sig={Uri.EscapeDataString(sig)}";
    }

    // TA-3.6 string-to-sign (version line = 2024-08-04) — identical to
    // <c>BlobSasMinterTest.Sign</c>, but with a caller-supplied protocol
    // line so the transport can re-sign <c>spr=https,http</c>.
    static string Sign(
        string permissions, string starts, string expires,
        string canonicalName, string protocol)
    {
        var stringToSign = string.Join('\n', new[]
        {
            permissions,
            starts,
            expires,
            canonicalName,
            string.Empty,     // identifier
            string.Empty,     // IP address range
            protocol,         // caller-supplied ("https" or "https,http")
            "2024-08-04",     // TA-3.6 "Version 2024"
            "b",              // resource: blob
            string.Empty,     // snapshot
            string.Empty,     // encryption scope
            string.Empty,     // cache control
            string.Empty,     // content disposition
            string.Empty,     // content encoding
            string.Empty,     // content language
            string.Empty,     // content type
        });
        using var hmac = new HMACSHA256(
            Convert.FromBase64String(AzuriteBuilder.AccountKey));
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

    static async Task<byte[]> ReadAllAsync(
        IBlobStore store, string container, string name)
    {
        await using var stream = await store.DownloadAsync(
            container, name);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    static string Unescape(string value) =>
        Uri.UnescapeDataString(value);

    static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in url.Query[1..].Split('&'))
        {
            var c = pair.IndexOf('='!);
            result[pair[..c]] = pair[(c + 1)..];
        }
        return result;
    }
}
