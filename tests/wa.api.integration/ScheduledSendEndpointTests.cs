using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;

namespace wa.api.integration;

/// <summary>feature.scheduling flipped on before the first request.</summary>
public class ScheduledSendEndpointTests() : ScheduledSendEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task ProUser_Schedule_201_StatusScheduled_RowAndOutbox() // FR-022-4
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        var due = DateTimeOffset.UtcNow.AddHours(2);

        using var response = await PostSendAsync(transferId, ScheduleBody(due), userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Scheduled", body.GetProperty("status").GetString());
        Assert.Equal(due.UtcDateTime, body.GetProperty("scheduledSendAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));

        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Scheduled, row.Status);
        Assert.NotNull(row.ScheduledSendAtUtc);
        Assert.Equal(due.UtcDateTime, row.ScheduledSendAtUtc!.Value, TimeSpan.FromSeconds(1));

        // transfer.created is emitted NOW, carrying scheduledSendAt (FR-022-4).
        var evt = await CreatedEventAsync(row.LinkId);
        Assert.NotNull(evt);
        using var envelope = JsonDocument.Parse(evt!.EnvelopeJson);
        var payload = envelope.RootElement.GetProperty("payload");
        Assert.Equal(due.UtcDateTime, payload.GetProperty("scheduledSendAt").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProUser_Scheduled_LinkIsLiveNow() // story AC: the link is live now
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task ProUser_Scheduled_PublicPage_CarriesUtcScheduledSendAt() // FR-022-6, EC-022-6 (UTC on the wire)
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        var due = DateTimeOffset.UtcNow.AddHours(2);
        using var send = await PostSendAsync(transferId, ScheduleBody(due), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("files").GetArrayLength());
        var wire = body.GetProperty("scheduledSendAt");
        Assert.Equal(JsonValueKind.String, wire.ValueKind);
        var parsed = wire.GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(due.UtcDateTime, parsed.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProUser_NoSchedule_PublicPage_ScheduledSendAtNull()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, "{\"emails\":[]}", userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var linkId = (await RowAsync(transferId)).LinkId;

        using var response = await GetAsync($"/api/v1/public/transfers/{linkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("scheduledSendAt").ValueKind);
    }

    [Fact]
    public async Task ProUser_Scheduled_CountsTowardActiveTransfers() // D-5, D-6
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);
        using var send = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);

        using var response = await GetAsync("/api/v1/billing/plan", userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("activeTransfers").GetProperty("used").GetInt32());
        Assert.True(body.GetProperty("features").GetProperty("scheduling").GetBoolean());
        var window = body.GetProperty("scheduleWindow");   // US-022-03 (D-4): the SPA reads the window from the server
        Assert.Equal(wa.domain.Transfers.ScheduleWindow.MinMinutes, window.GetProperty("minMinutes").GetInt32());
        Assert.Equal(wa.domain.Transfers.ScheduleWindow.MaxDays, window.GetProperty("maxDays").GetInt32());
    }

    [Fact]
    public async Task ProUser_Schedule_PlusTwoMinutes_422OutOfWindow_DraftUntouched() // AC-022-2 (server half)
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddMinutes(2)), userId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SCHEDULE_OUT_OF_WINDOW", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task ProUser_Schedule_PlusThirtyDays_422OutOfWindow_DraftUntouched() // AC-022-3 (server half)
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddDays(30)), userId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("SCHEDULE_OUT_OF_WINDOW", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task FreeUser_Schedule_403Forbidden_DraftUntouched() // FR-022-3
    {
        var userId = await SeedUserAsync("free");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task Guest_Schedule_403Forbidden() // FR-022-3: guests resolve to Free
    {
        var transferId = await SetupDraftTransferAsync(null);

        using var response = await PostSendAsync(transferId, ScheduleBody(DateTimeOffset.UtcNow.AddHours(2)), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Draft, row.Status);
        Assert.Null(await CreatedEventAsync(row.LinkId));
    }

    [Fact]
    public async Task InvalidScheduledSendAt_400Validation()
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, "{\"emails\":[],\"scheduledSendAt\":\"next tuesday\"}", userId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal(TransferStatus.Draft, (await RowAsync(transferId)).Status);
    }

    [Fact]
    public async Task ProUser_NoSchedule_StaysActive() // regression: absent field = send now
    {
        var userId = await SeedUserAsync("pro");
        var transferId = await SetupDraftTransferAsync(userId);

        using var response = await PostSendAsync(transferId, "{\"emails\":[]}", userId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("scheduledSendAt").ValueKind);
        var row = await RowAsync(transferId);
        Assert.Equal(TransferStatus.Active, row.Status);
        Assert.Null(row.ScheduledSendAtUtc);
    }
}
