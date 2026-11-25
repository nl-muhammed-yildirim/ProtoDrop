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

/// <summary>feature.search left at its seeded "false": q is ignored entirely on endpoint 14, and FR-011-3 stays exactly as it was.</summary>
public class TransferSearchEndpointGateOffTests() : TransferSearchEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task FlagOff_QIgnored_PlainChronologicalList() // story AC 3, API half — q=financial returns the plain list, newest first
    {
        var userId = await SeedUserAsync();
        var tOld = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow.AddDays(-10));   // matches "financial" by file name…
        await SeedFilesAsync(tOld, "old-financial.pdf");
        var tNew = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow);               // …but is older; the plain list ignores q.

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal([tNew.ToString("N"), tOld.ToString("N")], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task FlagOff_LongQ_Ignored_200() // FR-045-5: q is ignored entirely — no length check either, because the gate is off
    {
        var userId = await SeedUserAsync();

        using var response = await SearchAsync(userId, new string('a', SearchTerm.MaxLength + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FlagOff_Admin_LegacyQ_NoFileNameMatch() // D7: FR-011-3 unchanged — LinkId exact OR recipient substring only, no file/sender widening
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);   // file name contains "financial", but the legacy match never looks at it.
        await SeedFilesAsync(t1, "x-financial.pdf");

        using var response = await GetAsync("/api/v1/admin/transfers?q=financial", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }
}
