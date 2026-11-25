using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>Global feature.analytics gate left at its seeded "false": even a Pro user gets 404 FEATURE_DISABLED (FR-023-5).</summary>
public class TransferAnalyticsEndpointTests_GateOff() : TransferAnalyticsEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task GateOff_ProUser_404FeatureDisabled()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("FEATURE_DISABLED", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }
}
