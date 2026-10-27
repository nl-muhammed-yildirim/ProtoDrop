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

namespace wa.api.integration;

/// <summary>
/// T-002-04 exit checks for POST /api/v1/transfers/{id}/send (TA-4.2#3):
/// normalized recipients stored, link-only send with zero rows, transfer.created outbox payload.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same pattern as FinalizeEndpointTests.
/// </summary>
public class SendEndpointTests : IAsyncLifetime
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
        // TA-4.1.7 / EC-054-1: the outbox row commits via SbEventPublisher's own AppDbContextFactory context
        // BEFORE broker publish; an unreachable (dummy) connection string exercises that path deterministically.
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + _accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=dummy");

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

    // ---- AC-002-2: Send with "a@x.com, b@y.com " stores 2 normalized addresses ----

    [Fact]
    public async Task Send_Endpoint_NormalizesAndStoresRecipients()
    {
        var transferId = await SetupDraftTransferAsync();

        using var response = await PostSendAsync(transferId, new
        {
            emails = new[] { "A@X.COM, b@y.com " },
            senderName = "Alice",
            senderEmail = "alice@example.com"
        }, "key-send-normalize");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("Active", root.GetProperty("status").GetString());
        var recipients = root.GetProperty("recipients");
        Assert.Equal(2, recipients.GetArrayLength());
        Assert.Contains("a@x.com", recipients.EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Contains("b@y.com", recipients.EnumerateArray().Select(e => e.GetString()).ToArray());

        // DB assertions: 2 EmailRecipient rows, lowercased.
        var dbRecipients = await _db.Set<EmailRecipient>().Where(r => r.TransferId == transferId).ToListAsync();
        Assert.Equal(2, dbRecipients.Count);
        Assert.Contains(dbRecipients, r => r.Address == "a@x.com");
        Assert.Contains(dbRecipients, r => r.Address == "b@y.com");

        // Transfer is Active.
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.Status);
    }

    // ---- AC-002-3: Send with 0 emails → Status=1, zero EmailRecipient rows ----

    [Fact]
    public async Task Send_Endpoint_ZeroEmails_LinkOnly_NoRecipientRows()
    {
        var transferId = await SetupDraftTransferAsync();

        using var response = await PostSendAsync(transferId, new
        {
            emails = Array.Empty<string>()
        }, "key-send-zero");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        Assert.Equal("Active", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("recipients").GetArrayLength());

        // Zero EmailRecipient rows in DB.
        var count = await _db.Set<EmailRecipient>().CountAsync(r => r.TransferId == transferId);
        Assert.Equal(0, count);

        // Transfer is Active.
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(1, transfer.Status);
    }

    // ---- Exit check 3: transfer.created outbox row has exact TA-5.3 payload ----

    [Fact]
    public async Task Send_Endpoint_TransferCreated_OutboxRow_HasExactPayload()
    {
        var transferId = await SetupDraftTransferAsync();

        using var response = await PostSendAsync(transferId, new
        {
            emails = new[] { "a@x.com" },
            senderEmail = "sender@example.com"
        }, "key-send-outbox");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Find the outbox row for THIS transfer — envelope PartitionKey = transfer.LinkId (TA-5.2), unique per test.
        var dbTransfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        var domainEvent = await _db.Set<DomainEvent>()
            .FirstOrDefaultAsync(e => e.EventType == "transfer.created" && e.PartitionKey == dbTransfer.LinkId);
        Assert.NotNull(domainEvent);

        // Parse the envelope JSON and verify the payload shape.
        using var envelopeDoc = JsonDocument.Parse(domainEvent!.EnvelopeJson);
        var envelopeRoot = envelopeDoc.RootElement;

        // TA-5.2 envelope fields.
        Assert.Equal("transfer.created", envelopeRoot.GetProperty("eventType").GetString());
        Assert.Equal(1, envelopeRoot.GetProperty("version").GetInt32());

        // TA-5.3 payload shape: { transferId, linkId, totalBytes, fileCount, emails, senderEmail?, scheduledSendAt? }
        var payload = envelopeRoot.GetProperty("payload");
        var payloadKeys = payload.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("transferId", payloadKeys);
        Assert.Contains("linkId", payloadKeys);
        Assert.Contains("totalBytes", payloadKeys);
        Assert.Contains("fileCount", payloadKeys);
        Assert.Contains("emails", payloadKeys);

        // Verify values.
        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(transfer.LinkId, payload.GetProperty("linkId").GetString());
        Assert.Equal(transfer.TotalBytes, payload.GetProperty("totalBytes").GetInt64());
        Assert.Equal(transfer.FileCount, payload.GetProperty("fileCount").GetInt32());
        Assert.Equal(1, payload.GetProperty("emails").GetArrayLength());
        Assert.Equal("a@x.com", payload.GetProperty("emails")[0].GetString());
    }

    // ---- 404 for unknown transfer ----

    [Fact]
    public async Task Send_Endpoint_UnknownTransfer_Returns404()
    {
        using var response = await PostSendAsync(Guid.NewGuid(), new
        {
            emails = Array.Empty<string>()
        }, "key-send-404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- T-007-04 / AC-007-1: send-time size re-check rejects oversize finalized transfers ----

    [Fact]
    public async Task Send_Endpoint_FinalizedOversize_TransferFailsWithTransferSizeExceeded()
    {
        // AC-007-1: finalize does NOT check MAX_TRANSFER_SIZE (only quota/active-transfers/blob-truth).
        // A transfer with TotalBytes > 5GB can be finalized, but send must reject it with 422 TRANSFER_SIZE_EXCEEDED.
        
        // Step 1: Directly seed a Transfer in DB with TotalBytes exceeding MAX_TRANSFER_SIZE.
        const long OversizeBytes = 5_658_836_480L; // 5.2 GB > 5 GB Free MaxTransferSize (5_368_709_120)
        var transferId = Guid.NewGuid();
        
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "OVSZ1234", // Valid 8-char alphanumeric (TA-7.1)
            Status = 0, // Draft — ready to send
            TotalBytes = OversizeBytes,
            FileCount = 1,
            SenderName = "Guest",
            MaxDownloads = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Step 2: Attempt to send — should fail with 422 TRANSFER_SIZE_EXCEEDED.
        using var response = await PostSendAsync(transferId, new { emails = Array.Empty<string>() }, "key-send-oversize");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        
        // Step 3: Verify the error code and that both numbers appear in details.
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var root = doc.RootElement;
        
        // TA-4.1.3 closed list: TRANSFER_SIZE_EXCEEDED pinned at 422.
        Assert.Equal("TRANSFER_SIZE_EXCEEDED", root.GetProperty("code").GetString());
        Assert.Equal(422, root.GetProperty("status").GetInt32());

        // AC-007-1: details must contain both numbers — actual size (5.2 GB) and limit (5.0 GB).
        var details = root.GetProperty("details").GetString();
        Assert.NotNull(details);
        
        const long FreeMaxTransferSize = 5_368_709_120L; // 5 GB
        double actualGib = OversizeBytes / 1073741824.0; // 5.2 GB
        double limitGib = FreeMaxTransferSize / 1073741824.0; // 5.0 GB
        
        Assert.Contains(actualGib.ToString("F1") + " GB", details);
        Assert.Contains(limitGib.ToString("F1") + " GB", details);

        // Checks precede mutation — the transfer stayed Draft (Status=0).
        var updatedTransfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal(0, updatedTransfer.Status);
    }

    // ---- Helpers ----

    /// <summary>Create a draft transfer via the existing endpoints (draft → finalize). Returns the transfer ID.</summary>
    private async Task<Guid> SetupDraftTransferAsync()
    {
        // Create draft.
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = new[] { new { name = "test.txt", sizeBytes = 512L, contentType = "text/plain" } } });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileId = new Guid(draftDoc.RootElement.GetProperty("files")[0].GetProperty("fileId").GetString()!);

        // Upload a blob to staging — canonical TA-3.5 split (TA-3.5): the first path segment is the container,
        // the rest the blob name — AzureBlobStore.SplitPath reads it that way, so we must write it that way.
        var stagingPath = BlobPaths.StagingFile(draftId, fileId);
        var bytes = new byte[512];
        RandomNumberGenerator.Fill(bytes);
        var cut = stagingPath.IndexOf('/');
        using (var ms = new MemoryStream(bytes))
            await _svc.GetBlobContainerClient(stagingPath[..cut]).GetBlobClient(stagingPath[(cut + 1)..])
                .UploadAsync(ms, true, default);

        // Finalize.
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
