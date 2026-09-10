using System.Security.Cryptography;
using System.Text;
using wa.infrastructure.Blob;

namespace wa.application.unit;

/// <summary>
/// T-007b — TA-3.6 SAS minting values. Exercises <see cref="BlobSasMinter"/>
/// with a fixed clock and the Azurite dev key; minting is pure
/// HMAC-SHA256, so no storage call happens. Per case we assert:
///   (a) permissions (draft <c>cwr</c>, recipient <c>r</c>, <c>r</c>),
///   (b) TTL (draft 2 h incl. session start; recipient 30 min, no start),
///   (c) scope (<c>sr=b</c>, blob resource), <c>sv=2024-08-04</c>,
///       <c>spr=https</c>, absolute URL form,
///   (d) <c>sig</c> equals an independently recomputed HMAC-SHA256 over the
///       TA-3.6 string-to-sign (version line = 2024-08-04).
/// </summary>
public class BlobSasMinterTest
{
    const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    static readonly Uri AccountUrl =
        new("http://127.0.0.1:10000/devstoreaccount1");

    // Fixed clock — deterministic, no environmental time.
    static readonly DateTimeOffset Clock = new(
        new DateTime(2026, 2, 6, 15, 0, 0, DateTimeKind.Utc),
        TimeSpan.Zero);

    [Fact]
    public void DraftUpload_Cwr_TtlIs2h_BlobScope_AbsoluteUrl()
    {
        var minter = new BlobSasMinter(AccountUrl, AccountKey);
        var url = minter.MintDraftUpload("d1", "f1", Clock);

        var q = Query(url);
        // (a) permissions, (d) signed scope, version, protocol.
        Assert.Equal("cwr", q["sp"]);
        Assert.Equal("b", q["sr"]);
        Assert.Equal("2024-08-04", q["sv"]);
        Assert.Equal("https", q["spr"]);
        // (b) TTL: session start = now, expiry = now + 2 h.
        Assert.Equal("2026-02-06T15:00:00Z", Unescape(q["st"]));
        Assert.Equal("2026-02-06T17:00:00Z", Unescape(q["se"]));
        Assert.Equal(TimeSpan.FromHours(2),
            DateTimeOffset.Parse(Unescape(q["se"])) -
            DateTimeOffset.Parse(Unescape(q["st"])));
        // absolute account-URL form, TA-3.5 staging layout.
        Assert.True(url.IsAbsoluteUri);
        Assert.Equal("http://127.0.0.1:10000/staging/d1/f/f1",
            url.GetLeftPart(UriPartial.Path));
        // (d) independent signature (version line swapped to 2024-08-04).
        Assert.Equal(Sign("cwr",
            "2026-02-06T15:00:00Z", "2026-02-06T17:00:00Z",
            "/blob/devstoreaccount1/staging/d1/f/f1"),
            Unescape(q["sig"]));
    }

    [Fact]
    public void RecipientSingleFile_R_TtlIs30min_NoStart_BlobScope()
    {
        var minter = new BlobSasMinter(AccountUrl, AccountKey);
        var url = minter.MintRecipientFile("t1", "f1", Clock);

        var q = Query(url);
        Assert.Equal("r", q["sp"]);
        Assert.Equal("b", q["sr"]);
        Assert.Equal("2024-08-04", q["sv"]);
        Assert.Equal("https", q["spr"]);
        Assert.False(q.ContainsKey("st"), "recipient reads have no st");
        // (b) TTL: expiry = now + 30 min.
        Assert.Equal("2026-02-06T15:30:00Z", Unescape(q["se"]));
        Assert.Equal(TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse(Unescape(q["se"])) - Clock);
        Assert.Equal("http://127.0.0.1:10000/transfers/t1/files/f1",
            url.GetLeftPart(UriPartial.Path));
        Assert.Equal(Sign("r", null, "2026-02-06T15:30:00Z",
            "/blob/devstoreaccount1/transfers/t1/files/f1"),
            Unescape(q["sig"]));
    }

    [Fact]
    public void RecipientZip_R_TtlIs30min_AbsoluteUrl()
    {
        var minter = new BlobSasMinter(AccountUrl, AccountKey);
        var url = minter.MintRecipientZip("t1", Clock);

        var q = Query(url);
        Assert.Equal("r", q["sp"]);
        Assert.Equal("b", q["sr"]);
        Assert.Equal("2024-08-04", q["sv"]);
        Assert.Equal("https", q["spr"]);
        Assert.False(q.ContainsKey("st"));
        Assert.Equal("2026-02-06T15:30:00Z", Unescape(q["se"]));
        Assert.Equal(TimeSpan.FromMinutes(30),
            DateTimeOffset.Parse(Unescape(q["se"])) - Clock);
        // TA-3.5 zip path: transfers/{transferId}/all.zip.
        Assert.Equal("http://127.0.0.1:10000/transfers/t1/all.zip",
            url.GetLeftPart(UriPartial.Path));
        Assert.Equal(Sign("r", null, "2026-02-06T15:30:00Z",
            "/blob/devstoreaccount1/transfers/t1/all.zip"),
            Unescape(q["sig"]));
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // The TA-3.6 string-to-sign layout for a shared-key (account-key)
    // blob SAS: version line = 2024-08-04, resource line = b (blob scope).
    static string Sign(
        string permissions, string? starts,
        string expires, string canonicalName)
    {
        var stringToSign = string.Join('\n', new[]
        {
            permissions,
            starts ?? string.Empty,
            expires,
            canonicalName,
            string.Empty,        // identifier
            string.Empty,        // IP address range
            "https",             // protocol
            "2024-08-04",        // TA-3.6 "Version 2024"
            "b",                 // resource: blob
            string.Empty,        // snapshot
            string.Empty,        // encryption scope
            string.Empty,        // cache control
            string.Empty,        // content disposition
            string.Empty,        // content encoding
            string.Empty,        // content language
            string.Empty,        // content type
        });
        using var hmac =
            new HMACSHA256(Convert.FromBase64String(AccountKey));
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

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

    static string Unescape(string value)
        => Uri.UnescapeDataString(value);
}