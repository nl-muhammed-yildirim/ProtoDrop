using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.domain.Transfers;

namespace wa.api.integration;

/// <summary>feature.search flipped on before the first request (flags cache TTL).</summary>
public class TransferSearchEndpointTests() : TransferSearchEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task FileName_Financial_MatchFirst_HolidayAbsent() // story AC 1 / AC-045-1
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "q4-financial-report.pdf");
        await SeedRecipientAsync(t1, "a@b.com");
        var t2 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t2, "holiday.mp4");
        await SeedRecipientAsync(t2, "c@d.com");

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        // TA-4.2a wire: ids are 'n'-form — compare with N-form.
        Assert.Equal(t1.ToString("N"), body.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(t2.ToString("N"), body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Recipient_ExactEmail_Matches() // story AC 2 / FR-045-2 (real column: EmailRecipient.Address)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "notes.txt");
        await SeedRecipientAsync(t1, "a@b.com");

        using var response = await SearchAsync(userId, "a@b.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Sender_Name_Matches() // FR-045-2 (real column: Transfer.SenderName)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId, senderName: "Zorro Ltd");

        using var response = await SearchAsync(userId, "zorro");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Operators_AreLiteral() // AC-045-3 / FR-045-6: '*', '%', '_', '"' are literal characters, not LIKE operators
    {
        var userId = await SeedUserAsync();
        var t3 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t3, "report *.pdf");
        var t4 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t4, "report-final.pdf");

        using (var r1 = await SearchAsync(userId, "report *"))   // only the file whose name literally contains '*'
        {
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            var b1 = await ReadJsonAsync(r1);
            Assert.Single(b1.GetProperty("items").EnumerateArray(), i => t3.ToString("N") == i.GetProperty("id").GetString());
        }

        var t5 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t5, "50%_off.png");
        var t6 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t6, "500 off.png");   // '50%' as a LIKE pattern would match this; literally it must not

        using (var r2 = await SearchAsync(userId, "50%_"))
        {
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            var b2 = await ReadJsonAsync(r2);
            Assert.Single(b2.GetProperty("items").EnumerateArray(), i => t5.ToString("N") == i.GetProperty("id").GetString());
        }

        using (var r3 = await SearchAsync(userId, "\""))   // a lone double quote: 200 with an empty list
        {
            Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
            var b3 = await ReadJsonAsync(r3);
            Assert.Empty(b3.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task FileAndRecipientMatch_SingleRow() // EC-045-6: one row per transfer — de-dup by construction (rank is a CASE over EXISTS)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "acme-1.pdf", "acme-2.pdf");
        await SeedRecipientAsync(t1, "acme@x.com");

        using var response = await SearchAsync(userId, "acme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task FileMatch_OutranksRecipientMatch() // FR-045-3: rank file=3 > recipient=2 — the earlier-created file match still comes first
    {
        var userId = await SeedUserAsync();
        var t7 = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow.AddDays(-10));
        await SeedFilesAsync(t7, "zeta.pdf");
        var t8 = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow);
        await SeedFilesAsync(t8, "a.pdf");
        await SeedRecipientAsync(t8, "zeta@y.com");

        using var response = await SearchAsync(userId, "zeta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal([t7.ToString("N"), t8.ToString("N")], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task NeverCrossesOwners() // FR-045-7: owner scope in the predicate — search never crosses owners
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "a-financial.pdf");
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "secret-financial.pdf");

        using (var rA = await SearchAsync(userA, "financial"))
        {
            Assert.Equal(HttpStatusCode.OK, rA.StatusCode);
            var bA = await ReadJsonAsync(rA);
            Assert.DoesNotContain(tB.ToString("N"), bA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
            Assert.Contains(tA.ToString("N"), bA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        }

        using (var rB = await SearchAsync(userB, "financial"))
        {
            var bB = await ReadJsonAsync(rB);
            Assert.Single(bB.GetProperty("items").EnumerateArray(), i => tB.ToString("N") == i.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task DeletedTransfer_NotFound() // FR-045-7: not-deleted predicate — deleted rows never surface in search
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId, deleted: true);
        await SeedFilesAsync(t1, "gone-financial.pdf");

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task NoMatch_EmptyList() // EC-045-1: 200 with items [] and nextCursor null (not an error)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "notes.txt");

        using var response = await SearchAsync(userId, "zzz-no-match-zzz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Null(body.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task QTooLong_400() // EC-045-3: 101 chars after trim → 400 Q_TOO_LONG; exactly 100 is fine
    {
        var userId = await SeedUserAsync();

        using (var longResponse = await SearchAsync(userId, new string('a', SearchTerm.MaxLength + 1)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);
            Assert.Equal("Q_TOO_LONG", (await ReadJsonAsync(longResponse)).GetProperty("code").GetString());
        }

        using var okResponse = await SearchAsync(userId, new string('a', SearchTerm.MaxLength));
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    [Fact]
    public async Task Paging_SearchCursor() // FR-045-3: the search cursor is bound to its term — 26 rows over two pages of 25+1
    {
        var userId = await SeedUserAsync();
        for (var i = 0; i < 26; i++)
            await SeedFilesAsync(await SeedTransferAsync(userId), $"deck-{i}.pdf");

        using var first = await SearchAsync(userId, "deck");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body1 = await ReadJsonAsync(first);
        var page1Ids = body1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(25, page1Ids.Count);
        var nextCursor = body1.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(nextCursor));

        using (var second = await GetAsync($"/api/v1/transfers?q=deck&cursor={Uri.EscapeDataString(nextCursor!)}", userId))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var body2 = await ReadJsonAsync(second);
            var page2Ids = body2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
            Assert.Single(page2Ids);   // 26 total: the last row only — no id repeats across pages.
            Assert.DoesNotContain(page2Ids[0], page1Ids);
        }

        using (var foreign = await GetAsync($"/api/v1/transfers?q=other&cursor={Uri.EscapeDataString(nextCursor!)}", userId))
        {
            // The cursor's term differs from the current q → 400 VALIDATION (existing closed-list member).
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            var body = await ReadJsonAsync(foreign);
            Assert.Equal("VALIDATION", body.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Admin_FileName_CrossesOwners_ShowsOwner() // AC-045-2: endpoint 21 widens to file name / sender with the gate on; ownerEmail stays populated
    {
        var userA = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "a-financial.pdf");
        var userB = await SeedUserAsync();
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "b-financial.pdf");

        using var response = await GetAsync("/api/v1/admin/transfers?q=financial", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains(tA.ToString(), ids);   // endpoint 21 serializes Guids in the standard dashed form (TA-4.2a is n-form only on rows).
        Assert.Contains(tB.ToString(), ids);
        Assert.All(body.GetProperty("items").EnumerateArray(), i => Assert.False(string.IsNullOrEmpty(i.GetProperty("ownerEmail").GetString())));
    }

    [Fact]
    public async Task Admin_Recipient_CrossesOwners_ShowsOwner() // AC-045-2: admin searches the exact recipient "a@b.com" across owners — every transfer sent to that address is listed, owner email populated (EmailRecipient.Address match)
    {
        var userA = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "notes-a.txt");
        await SeedRecipientAsync(tA, "a@b.com");

        var userB = await SeedUserAsync();
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "notes-b.txt");
        await SeedRecipientAsync(tB, "a@b.com");   // same recipient address, different owner — must also surface.

        using var response = await GetAsync("/api/v1/admin/transfers?q=a%40b.com", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains(tA.ToString(), ids);   // dashed Guid form on endpoint 21 (TA-4.2a n-form is rows-only).
        Assert.Contains(tB.ToString(), ids);
        Assert.All(body.GetProperty("items").EnumerateArray(), i => Assert.False(string.IsNullOrEmpty(i.GetProperty("ownerEmail").GetString())));
    }

    [Fact]
    public async Task Admin_QTooLong_400() // EC-045-3 on endpoint 21: flag-on q over SearchTerm.MaxLength (100 chars) → 400 Q_TOO_LONG; exactly 100 is fine. Mirrors the gate-off FlagOff_LongQ_Ignored_200 contrast — here the gate is ON so the length check applies.
    {
        using (var longResponse = await GetAsync($"/api/v1/admin/transfers?q={new string('a', SearchTerm.MaxLength + 1)}", AdminUserId))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);
            Assert.Equal("Q_TOO_LONG", (await ReadJsonAsync(longResponse)).GetProperty("code").GetString());
        }

        using var okResponse = await GetAsync($"/api/v1/admin/transfers?q={new string('a', SearchTerm.MaxLength)}", AdminUserId);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }
}
