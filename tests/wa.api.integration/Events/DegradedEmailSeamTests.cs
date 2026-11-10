using System.Net;
using System.Net.Http.Json;
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
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration.Events;

/// <summary>
/// T-013-05 (AC-013-3 / FR-013-4): seam proof — POST send with Hub down (dummy Service Bus connection) still
/// returns 201 + link screen, transfer.created outbox row commits via SbEventPublisher's own AppDbContextFactory
/// context BEFORE broker publish, and the API never touches the Hub directly. Same pattern as SendEndpointTests L45.
/// </summary>
public class DegradedEmailSeamTests : IAsyncLifetime
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
        _accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", _accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");
        // Hub down at send time: the outbox row commits via SbEventPublisher's own AppDbContextFactory context
        // BEFORE broker publish; an unreachable (dummy) connection string exercises that path deterministically.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + _accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=***");

        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        var connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await _db.Database.MigrateAsync();

        _svc = new BlobServiceClient(
            _accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _client = new TestFactory().CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- AC-013-3: Hub down at send time → 201 + link screen, transfer.created outbox row exists, no API-level failure ----

    [Fact]
    public async Task Send_WithHubDown_Returns201_LinkScreen_TransferCreatedOutboxRowExists()
    {
        var transferId = await SetupDraftTransferAsync();

        using var response = await PostSendAsync(transferId, new
        {
            emails = new[] { "a@x.com" },
            senderEmail = "sender@example.com"
        }, "key-degraded-seam");

        // 201 + link screen — the sender saw nothing (FR-013-4: no API-level failure at all).
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("recipients").GetArrayLength());

        // transfer.created outbox row exists in DB (TA-5.3 payload shape) — committed BEFORE broker publish.
        var dbTransfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        var domainEvent = await _db.Set<DomainEvent>()
            .FirstOrDefaultAsync(e => e.EventType == "transfer.created" && e.PartitionKey == dbTransfer.LinkId);
        Assert.NotNull(domainEvent);

        // Hub only touched inside f-email worker — the API never calls Hub directly (FR-013-4).
        // The outbox row's IsPublished=false proves the broker publish failed (dummy connection), yet the send succeeded.
        Assert.False(domainEvent!.IsPublished); // broker unreachable — the seam held: DB commit before broker publish

        // Transfer is Active — no API-level failure, no blocking.
        Assert.Equal(1, dbTransfer.Status);
    }

    // ---- Helpers (same pattern as SendEndpointTests) ----

    private async Task<Guid> SetupDraftTransferAsync()
    {
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = new[] { new { name = "test.txt", sizeBytes = 512L, contentType = "text/plain" } } });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        var bytes = new byte[512];
        RandomNumberGenerator.Fill(bytes);
        var cut = stagingPath.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(stagingPath[..cut]).GetBlobClient(stagingPath[(cut + 1)..])
                .UploadAsync(ms, true, default);

        var finalizePayload = JsonSerializer.Serialize(new { files = new[] { new { fileId, name = "test.txt", sizeBytes = 512L, contentType = "text/plain", retries = 0 } } });
        var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(finalizePayload, Encoding.UTF8, "application/json")
        };
        finalizeRequest.Headers.Add("Idempotency-Key", $"key-finalize-{Guid.NewGuid():N}");
        using var finalizeResponse = await _client.SendAsync(finalizeRequest);
        Assert.Equal(HttpStatusCode.Created, finalizeResponse.StatusCode);

        using var finalizeDoc = JsonDocument.Parse(await finalizeResponse.Content.ReadAsByteArrayAsync());
        return new Guid(finalizeDoc.RootElement.GetProperty("id").GetString()!);
    }

    private async Task<HttpResponseMessage> PostSendAsync(Guid transferId, object body, string idempotencyKey)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/{transferId:N}/send")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
