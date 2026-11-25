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
using wa.application.Limits;
using wa.domain;

namespace wa.api.integration;

/// <summary>Global feature.scheduling gate left at its seeded "false": even a Pro user is refused (FR-022-3).</summary>
public class ScheduledSendEndpointTests_GateOff() : ScheduledSendEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task GateOff_ProUser_Schedule_403Forbidden()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }
}
