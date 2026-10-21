using System.Security.Cryptography;
using System.Text;
using wa.application.Blob;
using wa.infrastructure.Blob;
namespace wa.application.unit;

/// <summary>T-055-04 — FR-055-2..FR-055-6, TA-3.5/TA-3.6.</summary>
/// <para>
/// No live storage: the minter is pure HMAC over a frozen string-to-sign (TA-3.6).
/// Every purpose's SAS fields are pinned field-by-field against a fixed clock and a
/// fresh Azurite dev key (not secret in local setup — the test constructs it directly,
/// so no DI; ADR T-007b), plus an independent re-signing check per FR-055-6.
/// </para>
public class BlobSasMinterTest : IDisposable
{
    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string AccountUrl = "https://blob.local.test.example";

    private static readonly DateTimeOffset Now = new(2026, 2, 6, 15, 0, 0, TimeSpan.Zero);

    // DraftId/FileId/TransferId: the :N format (TA-3.1) — lowercase invariant hex without dashes.
    private const string DfId = "0f9d4a7e8c2b4e6d9a1b3c5d7e9f0a2b";
    private const string FldId = "1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d";
    private const string TrnId = "2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e";

    private readonly BlobSasMinter _minter;
    private byte[] _accountKey;

    public BlobSasMinterTest()
    {
        _accountKey = Convert.FromBase64String(Key);
        _minter = new BlobSasMinter(new Uri(AccountUrl), Key, () => Now);
    }

    [Fact]
    public void DraftUpload_SAS_pins_cwr_scope_two_hour_window_and_st()
    {
        var url = _minter.MintDraftUpload(DfId, FldId).ToString();
        var q = Query(url);

        Assert.Equal("cwr", q["sp"]);                       // TA-3.6 draft upload: blob scope cwr
        Assert.Equal("b", q["sr"]);                         // blob resource (TA-3.5)
        Assert.Equal("2024", q["sv"]);                      // sv = spec literal (TA-3.6 "Version 2024")
        Assert.Equal(Now.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ssZ"), q["se"]);   // 2h TTL (TA-3.6)
        Assert.Equal(Now.ToString("yyyy-MM-ddTHH:mm:ssZ"), q["st"]);               // st present for drafts
        Assert.True(url.StartsWith(AccountUrl), "URL must be absolute — FR-055-4");
        assertValidSignature(q, $"/blob/blob.local.test.example/staging/{DfId}/f/{FldId}");
    }

    [Fact]
    public void RecipientFile_SAS_pins_r_scope_30min_window_and_no_st()
    {
        var url = _minter.MintRecipientFile(TrnId, FldId).ToString();
        var q = Query(url);

        Assert.Equal("r", q["sp"]);                          // TA-3.6 recipient file: blob scope r
        Assert.Equal("b", q["sr"]);
        Assert.Equal("2024", q["sv"]);
        Assert.Equal(Now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"), q["se"]);  // 30min TTL (TA-3.6)
        Assert.False(q.ContainsKey("st"));                   // no st for recipients (TA-3.6)

        assertValidSignature(q, $"/blob/blob.local.test.example/transfers/{TrnId}/files/{FldId}");
    }

    /// <summary>
    /// T-003-06 (EC-003-3): the 3-arg mint with an RFC 5987 resp-content-disposition override must sign the rs-cd line
    /// into the string-to-sign. Azurite honors rs-cd, so a tampered sig would be rejected at stream time — this test
    /// pins that the signature is valid for EXACTLY this disposition (and not the empty one from the 2-arg overload).
    /// </summary>
    [Fact]
    public void RecipientFile_SAS_WithContentDisposition_PinsRsCdAndSignature()
    {
        const string fileName = "Q4 report (final) v2.pdf";
        var url = _minter.MintRecipientFile(TrnId, FldId, respContentDisposition: $"attachment;filename*=UTF-8''{Uri.EscapeDataString(fileName)}").ToString();
        var q = Query(url);

        Assert.Equal("r", q["sp"]);                          // TA-3.6 recipient file: blob scope r
        Assert.Equal("b", q["sr"]);                         // blob resource (TA-3.5)
        Assert.Equal("2024", q["sv"]);                      // sv = spec literal (TA-3.6 "Version 2024")
        Assert.Equal(Now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"), q["se"]);  // 30min TTL (TA-3.6)
        Assert.False(q.ContainsKey("st"));                   // no st for recipients (TA-3.6)

        // The content-disposition override is pinned bit-exact under the rscd token (EC-003-3: the browser's
        // Content-Disposition round-trips through this override — part of the string-to-sign).
        Assert.Equal($"attachment;filename*=UTF-8''{Uri.EscapeDataString(fileName)}", q["rscd"]);

        // Independent re-sign with the rs-cd line in the string-to-sign — proves the sig covers exactly this disposition.
        var sst = Sign(
            permissions: q["sp"], start: q.ContainsKey("st") ? q["st"] : "", expiry: q["se"],
            path: $"/blob/blob.local.test.example/transfers/{TrnId}/files/{FldId}",
            ipRange: q.GetValueOrDefault("sip", ""), protocol: "https", resourceType: "b",
            accountKey: _accountKey, respContentDisposition: q["rscd"]);
        Assert.Equal(Convert.ToBase64String(sst), q["sig"]); // FR-055-6 style — the emitted sig verifies against an independent HMAC
    }

    [Fact]
    public void RecipientZip_SAS_pins_r_scope_30min_window_and_no_st()
    {
        var url = _minter.MintRecipientZip(TrnId).ToString();
        var q = Query(url);
        Assert.Equal("r", q["sp"]);                          // TA-3.6 recipient zip: blob scope r
        Assert.Equal("2024", q["sv"]);
        Assert.Equal(Now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"), q["se"]);  // 30min TTL (TA-3.6)
        Assert.False(q.ContainsKey("st"));                   // no st for recipients (TA-3.6)

        assertValidSignature(q, $"/blob/blob.local.test.example/transfers/{TrnId}/all.zip");
    }

    private void assertValidSignature(Dictionary<string, string> q, string canonicalPath)
    {
        var sst = Sign(
            permissions: q["sp"], start: q.ContainsKey("st") ? q["st"] : "", expiry: q["se"], path: canonicalPath,
            ipRange: q.GetValueOrDefault("sip", ""), protocol: "https", resourceType: "b", accountKey: _accountKey);
        // FR-055-6 — the emitted sig verifies against an independent HMAC over the same string-to-sign.
        Assert.Equal(Convert.ToBase64String(sst), q["sig"]);
    }

    private static Dictionary<string, string> Query(string url)
    {
        var qs = url.Substring(url.IndexOf('?') + 1);
        return qs.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(kv => kv.Split('=', 2))
            .ToDictionary(p => p[0], p => Unescape(p[1]));
    }

    private static string Unescape(string s) => System.Net.WebUtility.UrlDecode(s);

    // Independent re-signing: HMAC-SHA256 over the exact TA-3.6 16-line string-to-sign (blob scope).
    // sv line is the spec literal "2024" (TA-3.6 — not the SDK's internal build date).
    // respContentDisposition is optional (T-003-06): when given, it substitutes for the empty content-disposition
    // line in the string-to-sign — the same slot the minter fills via builder.ContentDisposition before ToSasQueryParameters.
    private static byte[] Sign(string permissions, string start, string expiry, string path,
        string ipRange, string protocol, string resourceType, byte[] accountKey, string? respContentDisposition = null)
    {
        var sst = string.Join("\n",
            permissions, start, expiry, path, "", ipRange, protocol, "2024", resourceType,
            // 7 trailing lines (snapshot/encryption scope/cache control/content disposition/content encoding/content language/content type):
            "", "", "", respContentDisposition ?? "", "", "", "");

        using var h = new HMACSHA256(accountKey);
        return h.ComputeHash(Encoding.UTF8.GetBytes(sst));
    }

    public void Dispose() { }
}
